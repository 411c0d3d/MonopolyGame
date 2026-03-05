/* globals useState, useEffect, useCallback, useContext, Ctx, COLORS, SERVER_URL, gameHub, React */

// pages/home_page.js

/**
 * Home page — browse open games or join by ID.
 * Player name and admin role come from auth claims, not user input.
 * @param {{ user: object, isAdmin: boolean, onCreateAndJoin: function, onJoin: function, onAdmin: function, onLogout: function }} props
 */
function HomePage({user, isAdmin, onCreateAndJoin, onJoin, onAdmin, onLogout}) {
    const {toast} = useContext(Ctx);
    const [joinId, setJoinId]           = useState('');
    const [activeTab, setActiveTab]     = useState('browse');
    const [creating, setCreating]       = useState(false);
    const [games, setGames]             = useState([]);
    const [initialLoading, setInitialLoading] = useState(false);

    const fetchGames = useCallback((silent = false) => {
        if (!silent) { setInitialLoading(true); }
        fetch(`${SERVER_URL}/api/games`)
            .then(r => r.json())
            .then(data => { setGames(data); setInitialLoading(false); })
            .catch(() => setInitialLoading(false));
    }, []);

    useEffect(() => {
        if (activeTab !== 'browse') { return; }
        fetchGames(false);
        const interval = setInterval(() => fetchGames(true), 5000);
        return () => clearInterval(interval);
    }, [activeTab, fetchGames]);

    const handleCreate = () => {
        setCreating(true);
        fetch(`${SERVER_URL}/api/games`, {method: 'POST'})
            .then(r => r.json())
            .then(data => { onCreateAndJoin(data.gameId); setCreating(false); })
            .catch(() => { toast('Could not reach server', 'error'); setCreating(false); });
    };

    const handleJoinById = () => {
        if (!joinId.trim()) { toast('Enter a Game ID', 'error'); return; }
        onJoin(joinId.trim().toUpperCase());
    };

    const tabs = ['browse', 'join', ...(isAdmin ? ['admin'] : [])];

    return (
        <div className="page-enter">
            <Header page="home"/>
            <div className="center" style={{padding: '28px 18px'}}>

                {/* Hero */}
                <div style={{textAlign: 'center', marginBottom: 32}}>
                    <div className="big-icon">🎲</div>
                    <h1 className="htitle">Play <span>Monopoly</span> Online</h1>
                    <p style={{color: '#aaa', fontSize: 13, marginTop: 7}}>Real-time multiplayer · 2–8 players</p>
                </div>

                <div style={{width: '100%', maxWidth: 520}}>

                    {/* Logged-in user identity card */}
                    <div className="card" style={{marginBottom: 12, display: 'flex', alignItems: 'center', gap: 12}}>
                        <div className="pav" style={{background: COLORS[0], color: '#fff', flexShrink: 0}}>
                            {(user?.name || '?')[0].toUpperCase()}
                        </div>
                        <div style={{flex: 1, minWidth: 0}}>
                            <div style={{fontWeight: 700, fontSize: 14, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'}}>
                                {user?.name}
                            </div>
                            <div style={{fontSize: 11, color: '#aaa', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'}}>
                                {user?.email}
                                {isAdmin && <span className="badge bg-yellow" style={{marginLeft: 6}}>Admin</span>}
                            </div>
                        </div>
                        <button className="btn btn-ghost btn-sm" onClick={onLogout} style={{flexShrink: 0}}>
                            Sign out
                        </button>
                    </div>

                    {/* Tab bar */}
                    <div style={{display: 'flex', gap: 5, marginBottom: 11}}>
                        {tabs.map(tab => (
                            <button
                                key={tab}
                                className={`btn btn-sm ${activeTab === tab ? 'btn-ink' : 'btn-ghost'}`}
                                onClick={() => setActiveTab(tab)}
                            >
                                {tab === 'browse' ? '🎮 Browse' : tab === 'join' ? '# Join by ID' : '⚙ Admin'}
                            </button>
                        ))}
                    </div>

                    {/* Browse tab */}
                    {activeTab === 'browse' && (
                        <div className="card">
                            <div style={{display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 13}}>
                                <div className="slabel" style={{margin: 0}}>Open Games</div>
                                <button className="btn btn-sm btn-ghost" onClick={() => fetchGames(false)}>↻ Refresh</button>
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
                                    <div key={game.gameId} className="game-row" onClick={() => onJoin(game.gameId)}>
                                        <div>
                                            <code style={{fontFamily: 'monospace', fontWeight: 700, letterSpacing: 2, fontSize: 14}}>
                                                {game.gameId}
                                            </code>
                                            <div style={{fontSize: 11, color: '#aaa', marginTop: 2}}>
                                                Host: {game.hostName}&nbsp;·&nbsp;{game.playerCount}/{game.maxPlayers} players
                                            </div>
                                        </div>
                                        <div style={{marginLeft: 'auto', display: 'flex', gap: 6, alignItems: 'center'}}>
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
                                    ? <span style={{display: 'flex', alignItems: 'center', gap: 7}}><div className="spin"/>Creating…</span>
                                    : '＋ Create New Game'
                                }
                            </button>
                        </div>
                    )}

                    {/* Join by ID tab */}
                    {activeTab === 'join' && (
                        <div className="card">
                            <div className="ig">
                                <label className="il">Game ID</label>
                                <input
                                    className="input"
                                    placeholder="e.g. AB12CD34"
                                    value={joinId}
                                    onChange={e => setJoinId(e.target.value.toUpperCase())}
                                    onKeyDown={e => { if (e.key === 'Enter') { handleJoinById(); } }}
                                    style={{fontFamily: 'monospace', letterSpacing: 3, fontSize: 18}}
                                    maxLength={8}
                                />
                            </div>
                            <button className="btn btn-green btn-full btn-lg" onClick={handleJoinById}>
                                → Join Game
                            </button>
                        </div>
                    )}

                    {/* Admin tab — only rendered when user has Admin role */}
                    {activeTab === 'admin' && (
                        <div className="card">
                            <p style={{color: '#aaa', fontSize: 13, marginBottom: 14}}>
                                You have admin access. Open the panel to manage games and server state.
                            </p>
                            <button className="btn btn-ink btn-full" onClick={onAdmin}>
                                ⚙ Open Admin Panel
                            </button>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}