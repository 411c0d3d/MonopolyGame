/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */

// components/home_page.js — depends on constants.js, signalr.js, header.js.

/**
 * Home page with browse/join/admin tabs and game creation flow.
 * @param {{ onCreateAndJoin: function, onJoin: function, onAdminLogin: function }} props
 */
function HomePage({onCreateAndJoin, onJoin, onAdminLogin}) {
    const {toast} = useContext(Ctx);
    const [playerName, setPlayerName] = useState('');
    const [joinId, setJoinId] = useState('');
    const [activeTab, setActiveTab] = useState('browse');
    const [creating, setCreating] = useState(false);
    const [games, setGames] = useState([]);
    const [initialLoading, setInitialLoading] = useState(false);
    const [adminKey, setAdminKey] = useState('');

    // Separate initial load (shows spinner) from background poll (silent update)
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

    useEffect(() => {
        if (activeTab !== 'browse') {
            return;
        }
        fetchGames(false);
        const interval = setInterval(() => fetchGames(true), 5000);
        return () => clearInterval(interval);
    }, [activeTab, fetchGames]);

    const handleCreate = () => {
        if (!playerName.trim()) {
            toast('Enter your name', 'error');
            return;
        }
        setCreating(true);
        fetch(`${SERVER_URL}/api/games`, {method: 'POST'})
            .then(r => r.json())
            .then(data => {
                onCreateAndJoin(data.gameId, playerName.trim());
                setCreating(false);
            })
            .catch(() => {
                toast('Could not reach server', 'error');
                setCreating(false);
            });
    };

    const handleJoinById = () => {
        if (!playerName.trim()) {
            toast('Enter your name', 'error');
            return;
        }
        if (!joinId.trim()) {
            toast('Enter a Game ID', 'error');
            return;
        }
        onJoin(joinId.trim().toUpperCase(), playerName.trim());
    };

    const handleJoinListed = (gid) => {
        if (!playerName.trim()) {
            toast('Enter your name first', 'error');
            return;
        }
        onJoin(gid, playerName.trim());
    };

    return (
        <div className="page-enter">
            <Header page="home"/>
            <div className="center" style={{padding: '28px 18px'}}>
                <div style={{textAlign: 'center', marginBottom: 32}}>
                    <div className="big-icon">🎲</div>
                    <h1 className="htitle">Play <span>Monopoly</span> Online</h1>
                    <p style={{color: '#aaa', fontSize: 13, marginTop: 7}}>Real-time multiplayer · 2–8 players</p>
                </div>

                <div style={{width: '100%', maxWidth: 520}}>
                    <div className="card" style={{marginBottom: 12}}>
                        <div className="ig" style={{marginBottom: 0}}>
                            <label className="il">Your Name</label>
                            <input
                                className="input"
                                placeholder="e.g. Bob"
                                value={playerName}
                                onChange={e => setPlayerName(e.target.value)}
                                maxLength={20}
                            />
                        </div>
                    </div>

                    <div style={{display: 'flex', gap: 5, marginBottom: 11}}>
                        {['browse', 'join', 'admin'].map(tab => (
                            <button
                                key={tab}
                                className={`btn btn-sm ${activeTab === tab ? 'btn-ink' : 'btn-ghost'}`}
                                onClick={() => setActiveTab(tab)}
                            >
                                {tab === 'browse' ? '🎮 Browse' : tab === 'join' ? '# Join by ID' : '⚙ Admin'}
                            </button>
                        ))}
                    </div>

                    {activeTab === 'browse' && (
                        <div className="card">
                            <div style={{
                                display: 'flex',
                                justifyContent: 'space-between',
                                alignItems: 'center',
                                marginBottom: 13
                            }}>
                                <div className="slabel" style={{margin: 0}}>Open Games</div>
                                <button className="btn btn-sm btn-ghost" onClick={() => fetchGames(false)}>↻ Refresh
                                </button>
                            </div>
                            {initialLoading && (
                                <div style={{display: 'flex', justifyContent: 'center', padding: 18}}>
                                    <div className="spin"/>
                                </div>
                            )}
                            {!initialLoading && games.length === 0 && (
                                <div style={{color: '#ccc', fontSize: 13, textAlign: 'center', padding: '14px 0'}}>
                                    No open games. Create one!
                                </div>
                            )}
                            <div className="game-browser">
                                {games.map(game => (
                                    <div key={game.gameId} className="game-row"
                                         onClick={() => handleJoinListed(game.gameId)}>
                                        <div>
                                            <code style={{
                                                fontFamily: 'monospace',
                                                fontWeight: 700,
                                                letterSpacing: 2,
                                                fontSize: 14
                                            }}>
                                                {game.gameId}
                                            </code>
                                            <div style={{fontSize: 11, color: '#aaa', marginTop: 2}}>
                                                Host: {game.hostName} &nbsp;·&nbsp; {game.playerCount}/{game.maxPlayers} players
                                            </div>
                                        </div>
                                        <div
                                            style={{marginLeft: 'auto', display: 'flex', gap: 6, alignItems: 'center'}}>
                                            <span className="badge bg-green">Waiting</span>
                                            <span style={{fontSize: 17}}>→</span>
                                        </div>
                                    </div>
                                ))}
                            </div>
                            <button
                                className="btn btn-gold btn-full"
                                onClick={handleCreate}
                                disabled={creating}
                                style={{marginTop: 10}}
                            >
                                {creating
                                    ? <span style={{display: 'flex', alignItems: 'center', gap: 7}}><div
                                        className="spin"/>Creating…</span>
                                    : '＋ Create New Game'
                                }
                            </button>
                        </div>
                    )}

                    {activeTab === 'join' && (
                        <div className="card">
                            <div className="ig">
                                <label className="il">Game ID</label>
                                <input
                                    className="input"
                                    placeholder="e.g. AB12CD34"
                                    value={joinId}
                                    onChange={e => setJoinId(e.target.value.toUpperCase())}
                                    onKeyDown={e => {
                                        if (e.key === 'Enter') {
                                            handleJoinById();
                                        }
                                    }}
                                    style={{fontFamily: 'monospace', letterSpacing: 3, fontSize: 18}}
                                    maxLength={8}
                                />
                            </div>
                            <button className="btn btn-green btn-full btn-lg" onClick={handleJoinById}>
                                → Join Game
                            </button>
                        </div>
                    )}

                    {activeTab === 'admin' && (
                        <div className="card">
                            <div className="ig">
                                <label className="il">Admin Key</label>
                                <input
                                    className="input"
                                    type="password"
                                    placeholder="Enter admin key"
                                    value={adminKey}
                                    onChange={e => setAdminKey(e.target.value)}
                                    onKeyDown={e => {
                                        if (e.key === 'Enter') {
                                            onAdminLogin(adminKey);
                                        }
                                    }}
                                />
                            </div>
                            <button className="btn btn-ink btn-full" onClick={() => onAdminLogin(adminKey)}>
                                ⚙ Enter Admin Panel
                            </button>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}