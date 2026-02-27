/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */

// components/admin_page.js — depends on constants.js, header.js.

/**
 * Admin control panel for monitoring and managing active games.
 * @param {{ adminKey: string, onBack: function }} props
 */
function AdminPage({adminKey, onBack}) {
    const {toast} = useContext(Ctx);
    const [games, setGames] = useState([]);
    const [selectedId, setSelectedId] = useState(null);
    const [gameDetail, setGameDetail] = useState(null);
    const [loading, setLoading] = useState(false);
    const [adminConn, setAdminConn] = useState(null);

    const fetchGames = () => {
        setLoading(true);
        fetch(`${SERVER_URL}/api/games`)
            .then(r => r.json())
            .then(data => {
                setGames(data);
                setLoading(false);
            })
            .catch(() => {
                toast('Could not load games', 'error');
                setLoading(false);
            });
    };

    useEffect(() => {
        fetchGames();

        const conn = new signalR.HubConnectionBuilder()
            .withUrl(`${SERVER_URL}/admin-hub`)
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        conn.on('GameDetails', detail => setGameDetail(detail));
        conn.on('Error', msg => toast(msg, 'error'));
        conn.on('GameForceEnded', () => {
            toast('Game ended', 'success');
            setSelectedId(null);
            setGameDetail(null);
            fetchGames();
        });
        conn.on('GamePaused', () => {
            toast('Game paused', 'success');
            fetchGames();
        });
        conn.on('GameResumed', () => {
            toast('Game resumed', 'success');
            fetchGames();
        });
        conn.on('PlayerKicked', () => toast('Player kicked', 'success'));

        conn.start().catch(() => toast('Admin hub failed to connect', 'error'));
        setAdminConn(conn);

        return () => conn.stop();
    }, []);

    /**
     * Invokes an admin hub method with the stored admin key.
     * @param {string} method
     * @param {string} gid
     * @param {...any} rest
     */
    const adminAction = (method, gid, ...rest) => {
        if (!adminConn) {
            toast('Admin hub not connected', 'error');
            return;
        }
        adminConn.invoke(method, gid, adminKey, ...rest)
            .catch(e => toast(e.message || 'Admin action failed', 'error'));
    };

    const selectGame = (gid) => {
        setSelectedId(gid);
        setGameDetail(null);
        adminAction('GetGameDetails', gid);
    };

    const statusBadgeClass = (status) => {
        return {
            Waiting: 'bg-yellow',
            InProgress: 'bg-green',
            Paused: 'bg-red',
            Finished: 'bg-gray'
        }[status] || 'bg-gray';
    };

    return (
        <div className="page-enter">
            <Header page="admin"/>
            <div className="alayout">
                <div className="aside">
                    <div style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        marginBottom: 13
                    }}>
                        <div className="slabel" style={{margin: 0}}>All Games</div>
                        <button className="btn btn-sm btn-ghost" onClick={fetchGames}>↻</button>
                    </div>
                    {loading && <div className="spin" style={{margin: '18px auto'}}/>}
                    {!loading && games.length === 0 && (
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
                                <span className={`badge ${statusBadgeClass(game.status)}`}>{game.status}</span>
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
                                        className={`badge ${statusBadgeClass(gameDetail.status)}`}>{gameDetail.status}</span>
                                )}
                            </div>

                            <div style={{display: 'flex', gap: 9, flexWrap: 'wrap', marginBottom: 22}}>
                                <button className="btn btn-ghost btn-sm" onClick={() => {
                                    setGameDetail(null);
                                    adminAction('GetGameDetails', selectedId);
                                }}>↻ Refresh
                                </button>
                                <button className="btn btn-warn btn-sm"
                                        onClick={() => adminAction('PauseGame', selectedId)}>⏸ Pause
                                </button>
                                <button className="btn btn-green btn-sm"
                                        onClick={() => adminAction('ResumeGame', selectedId)}>▶ Resume
                                </button>
                                <button className="btn btn-red btn-sm" onClick={() => {
                                    if (confirm('Force end this game?')) {
                                        adminAction('ForceEndGame', selectedId);
                                    }
                                }}>⛔ Force End
                                </button>
                            </div>

                            {!gameDetail && (
                                <div style={{display: 'flex', gap: 9, alignItems: 'center', color: '#bbb'}}>
                                    <div className="spin"/>
                                    Loading…
                                </div>
                            )}

                            {gameDetail && (
                                <>
                                    <div className="card" style={{marginBottom: 14}}>
                                        <div className="slabel">Stats</div>
                                        <div style={{display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 10}}>
                                            {[
                                                ['Turn', gameDetail.turn],
                                                ['Players', gameDetail.playerCount],
                                                ['Status', gameDetail.status],
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
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: 9,
                                                padding: '9px 0',
                                                borderBottom: '1px solid var(--border)'
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
                                                        <button
                                                            className="btn btn-red btn-sm"
                                                            onClick={() => {
                                                                if (confirm(`Kick ${player.name}?`)) {
                                                                    adminAction('KickPlayer', selectedId, player.id);
                                                                }
                                                            }}
                                                        >
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