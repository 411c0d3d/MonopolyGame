/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */

// components/admin_page.js — depends on constants.js, header.js.

/**
 * Admin control panel for monitoring and managing active games.
 * @param {{ onBack: function }} props
 */
function AdminPage({onBack}) {
    const {toast} = useContext(Ctx);
    const [games, setGames] = useState([]);
    const [selectedId, setSelectedId] = useState(null);
    const [gameDetail, setGameDetail] = useState(null);
    const [initialLoading, setInitialLoading] = useState(false);
    const [detailLoading, setDetailLoading] = useState(false);
    const adminConnRef = React.useRef(null);
    const selectedIdRef = React.useRef(null);
    const [botCount, setBotCount] = useState(1);

    // Mirror of home_page: silent=true skips the spinner for background polls
    const fetchGames = useCallback((silent = false) => {
        if (!silent) {
            setInitialLoading(true);
        }
        fetch(`${SERVER_URL}/api/games`)
            .then(r => r.json())
            .then(data => {
                setGames(data);
                setInitialLoading(false);
            })
            .catch(() => {
                setInitialLoading(false);
            });
    }, []);

    const refreshDetail = useCallback((silent = false) => {
        const gid = selectedIdRef.current;
        if (!gid) {
            return;
        }
        const conn = adminConnRef.current;
        if (!conn || conn.state !== signalR.HubConnectionState.Connected) {
            return;
        }
        if (!silent) {
            setDetailLoading(true);
        }
        conn.invoke('GetGameDetails', gid)
            .catch(() => {
            })
            .finally(() => {
                if (!silent) {
                    setDetailLoading(false);
                }
            });
    }, []);

    useEffect(() => {
        selectedIdRef.current = selectedId;
    }, [selectedId]);

    // Poll games list every 5s, same cadence as home page browse tab
    useEffect(() => {
        fetchGames(false);
        const interval = setInterval(() => fetchGames(true), 5000);
        return () => clearInterval(interval);
    }, [fetchGames]);

    // Poll selected game detail every 5s when a game is selected
    useEffect(() => {
        if (!selectedId) {
            return;
        }
        const interval = setInterval(() => refreshDetail(true), 5000);
        return () => clearInterval(interval);
    }, [selectedId, refreshDetail]);

    useEffect(() => {
        const conn = new signalR.HubConnectionBuilder()
            .withUrl(`${SERVER_URL}/admin-hub`, {
                accessTokenFactory: () => authService.getToken(),
            })
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        conn.on('GameDetails', detail => {
            setGameDetail(detail);
            setDetailLoading(false);
        });
        conn.on('Error', msg => toast(msg, 'error'));

        console.log('[AdminHub] useEffect fired, _account=', authService._account);
        authService.getToken()
            .then(token => {
                console.log('[AdminHub] getToken resolved, token=', token ? token.substring(0, 20) + '...' : null);
                if (!token) {
                    return;
                }
                conn.start().catch(e => console.error('[AdminHub] connect failed:', e));
            })
            .catch(e => console.error('[AdminHub] getToken threw:', e));

        adminConnRef.current = conn;

        return () => conn.stop();
    }, []);

    /** Invokes an admin hub method; returns the promise so callers can chain .then(). */
    const adminInvoke = (method, ...args) => {
        const conn = adminConnRef.current;
        if (!conn || conn.state !== signalR.HubConnectionState.Connected) {
            toast('Admin hub not connected', 'error');
            return Promise.reject(new Error('Not connected'));
        }
        return conn.invoke(method, ...args)
            .catch(e => {
                toast(e.message || 'Admin action failed', 'error');
                throw e;
            });
    };

    const selectGame = (gid) => {
        setSelectedId(gid);
        selectedIdRef.current = gid;
        setGameDetail(null);
        setDetailLoading(true);
        adminInvoke('GetGameDetails', gid).catch(() => {
        });
    };

    const handlePause = () => {
        adminInvoke('PauseGame', selectedId)
            .then(() => {
                toast('Game paused', 'success');
                fetchGames(true);
                refreshDetail(false);
            })
            .catch(() => {
            });
    };

    const handleResume = () => {
        adminInvoke('ResumeGame', selectedId)
            .then(() => {
                toast('Game resumed', 'success');
                fetchGames(true);
                refreshDetail(false);
            })
            .catch(() => {
            });
    };

    const handleForceEnd = () => {
        if (!confirm('Force end this game?')) {
            return;
        }
        adminInvoke('ForceEndGame', selectedId)
            .then(() => {
                toast('Game ended', 'success');
                setSelectedId(null);
                setGameDetail(null);
                fetchGames(false);
            })
            .catch(() => {
            });
    };

    const handleKick = (player) => {
        if (!confirm(`Kick ${player.name}?`)) {
            return;
        }
        adminInvoke('KickPlayer', selectedId, player.id)
            .then(() => {
                toast('Player kicked', 'success');
                refreshDetail(false);
            })
            .catch(() => {
            });
    };

    const handleAddBots = () => {
        if (botCount < 1) {
            return;
        }
        adminInvoke('AddBotToGame', selectedId, botCount)
            .then(() => {
                toast(`${botCount} bot(s) added`, 'success');
                refreshDetail(false);
                fetchGames(true);
            })
            .catch(() => {
            });
    };

    const GAME_STATUS = {0: 'Waiting', 1: 'InProgress', 2: 'Finished', 3: 'Paused'};
    const resolveStatus = (s) => typeof s === 'number' ? GAME_STATUS[s] : s;

    const statusBadgeClass = (status) => ({
        Waiting: 'bg-yellow',
        InProgress: 'bg-green',
        Paused: 'bg-red',
        Finished: 'bg-gray'
    })[status] || 'bg-gray';

    return (
        <div className="page-enter">
            <Header page="admin" onBack={onBack}/>
            <div className="alayout">
                <div className="aside">
                    <div style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        marginBottom: 11
                    }}>
                        <div className="slabel" style={{margin: 0}}>All Games</div>
                        <button className="btn btn-sm btn-ghost" onClick={() => fetchGames(false)}>↻</button>
                    </div>
                    {initialLoading && <div className="spin" style={{margin: '18px auto'}}/>}
                    {!initialLoading && games.length === 0 && (
                        <div style={{fontSize: 11, color: '#ccc'}}>No games.</div>
                    )}
                    {games.map(game => (
                        <div
                            key={game.gameId}
                            className={`admin-game-row${selectedId === game.gameId ? ' sel' : ''}`}
                            onClick={() => selectGame(game.gameId)}
                        >
                            <code style={{fontFamily: 'monospace', fontWeight: 700, fontSize: 13, letterSpacing: 1}}>
                                {game.gameId}
                            </code>
                            <div style={{display: 'flex', gap: 5, alignItems: 'center', marginTop: 5}}>
                                <span className={`badge ${statusBadgeClass(resolveStatus(game.status))}`}>{resolveStatus(game.status)}</span>
                                <span style={{fontSize: 10, color: '#aaa'}}>{game.playerCount} players</span>
                            </div>
                        </div>
                    ))}
                    <div className="div"/>
                    <button className="btn btn-ghost btn-sm btn-full" onClick={onBack}>← Back to Home</button>
                </div>

                <div className="amain">
                    {!selectedId && (
                        <div style={{color: '#ccc', marginTop: 50, textAlign: 'center'}}>
                            <div style={{fontSize: 36, marginBottom: 9}}>⚙</div>
                            Select a game from the sidebar
                        </div>
                    )}

                    {selectedId && (
                        <>
                            <div style={{display: 'flex', alignItems: 'center', gap: 11, marginBottom: 18}}>
                                <h2 style={{fontSize: 19, fontWeight: 900}}>
                                    <code style={{letterSpacing: 2}}>{selectedId}</code>
                                </h2>
                                {gameDetail && (
                                    <span
                                        className={`badge ${statusBadgeClass(resolveStatus(gameDetail.status))}`}>{resolveStatus(gameDetail.status)}</span>
                                )}
                            </div>

                            <div style={{display: 'flex', gap: 9, flexWrap: 'wrap', marginBottom: 22}}>
                                <button className="btn btn-ghost btn-sm" onClick={() => refreshDetail(false)}>↻
                                    Refresh
                                </button>
                                {(() => {
                                    const status = resolveStatus(gameDetail?.status ?? games.find(g => g.gameId === selectedId)?.status);
                                    return (<>
                                        <button
                                            className="btn btn-warn btn-sm"
                                            onClick={handlePause}
                                            disabled={status !== 'InProgress'}
                                            style={{opacity: status !== 'InProgress' ? 0.35 : 1}}
                                        >⏸ Pause</button>
                                        <button
                                            className="btn btn-green btn-sm"
                                            onClick={handleResume}
                                            disabled={status !== 'Paused'}
                                            style={{opacity: status !== 'Paused' ? 0.35 : 1}}
                                        >▶ Resume</button>
                                    </>);
                                })()}
                                <button className="btn btn-red btn-sm" onClick={handleForceEnd}>⛔ Force End</button>
                            </div>

                            {detailLoading && !gameDetail && (
                                <div style={{display: 'flex', gap: 9, alignItems: 'center', color: '#bbb'}}>
                                    <div className="spin"/>
                                    Loading…
                                </div>
                            )}

                            {gameDetail && (
                                <>
                                    {resolveStatus(gameDetail.status) !== 'Finished' && (
                                        <div className="card" style={{marginBottom: 14}}>
                                            <div className="slabel">🤖 Add Bots</div>
                                            <div style={{display: 'flex', alignItems: 'center', gap: 9}}>
                                                <button className="btn btn-ghost btn-sm"
                                                        style={{padding: '3px 10px', fontSize: 15}}
                                                        onClick={() => setBotCount(c => Math.max(1, c - 1))}>−
                                                </button>
                                                <span style={{
                                                    fontSize: 13,
                                                    fontWeight: 700,
                                                    minWidth: 22,
                                                    textAlign: 'center'
                                                }}>{botCount}</span>
                                                <button className="btn btn-ghost btn-sm"
                                                        style={{padding: '3px 10px', fontSize: 15}}
                                                        onClick={() => setBotCount(c => Math.min(7, c + 1))}>+
                                                </button>
                                                <span style={{
                                                    fontSize: 11,
                                                    color: '#aaa'
                                                }}>bot{botCount !== 1 ? 's' : ''}</span>
                                                <button className="btn btn-ink btn-sm" style={{marginLeft: 'auto'}}
                                                        onClick={handleAddBots}>
                                                    + Add
                                                </button>
                                            </div>
                                        </div>
                                    )}

                                    <div className="card" style={{marginBottom: 14}}>
                                        <div className="slabel">Stats</div>
                                        <div style={{display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 10}}>
                                            {[
                                                ['Turn', gameDetail.turn],
                                                ['Players', gameDetail.playerCount],
                                                ['Status', resolveStatus(gameDetail.status)],
                                            ].map(([label, value]) => (
                                                <div key={label} style={{
                                                    textAlign: 'center',
                                                    padding: 9,
                                                    background: 'var(--cream)',
                                                    borderRadius: 8
                                                }}>
                                                    <div style={{fontSize: 18, fontWeight: 900}}>{value}</div>
                                                    <div style={{fontSize: 10, color: '#aaa'}}>{label}</div>
                                                </div>
                                            ))}
                                        </div>
                                    </div>

                                    <div className="card" style={{marginBottom: 14}}>
                                        <div className="slabel">Players</div>
                                        {(gameDetail.players || []).map((player, i) => (
                                            <div key={player.id} style={{
                                                display: 'flex', alignItems: 'center', gap: 9,
                                                padding: '9px 0', borderBottom: '1px solid var(--border)'
                                            }}>
                                                <div className="pav" style={{
                                                    background: COLORS[i % COLORS.length],
                                                    color: '#fff',
                                                    width: 28,
                                                    height: 28,
                                                    fontSize: 11
                                                }}>
                                                    {player.name[0]}
                                                </div>
                                                <div style={{flex: 1}}>
                                                    <div style={{fontWeight: 600, fontSize: 12}}>{player.name}</div>
                                                    <div style={{fontSize: 10, color: '#aaa'}}>
                                                        ${player.cash?.toLocaleString()} · Pos {player.position}
                                                        {player.isInJail && ' · ⛓'}
                                                        {player.isBankrupt && ' · 💀'}
                                                        {!player.isConnected && ' · 🔴'}
                                                    </div>
                                                </div>
                                                <div style={{display: 'flex', gap: 5, alignItems: 'center'}}>
                                                    <span className="badge bg-gray">{player.propertyCount} props</span>
                                                    {!player.isBankrupt && (
                                                        <button className="btn btn-red btn-sm"
                                                                onClick={() => handleKick(player)}>
                                                            Kick
                                                        </button>
                                                    )}
                                                </div>
                                            </div>
                                        ))}
                                    </div>

                                    <div className="card">
                                        <div className="slabel">Recent Events</div>
                                        <div className="elog">
                                            {(gameDetail.recentLogs || []).slice().reverse().map((entry, i) => (
                                                <div key={i} className="eitem">{entry}</div>
                                            ))}
                                        </div>
                                    </div>
                                </>
                            )}
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}