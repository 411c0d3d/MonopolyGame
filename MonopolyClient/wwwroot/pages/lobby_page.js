/* globals useState, useEffect, useRef, useContext, Ctx, COLORS, gameHub, React */

// pages/lobby_page.js

/**
 * Pre-game lobby showing players, settings, and the start button.
 * @param {{ gameId: string, userId: string, playerName: string, gameState: any, onStart: function, onLeave: function, isCreator: boolean, isAdmin: boolean, onAdmin: function }} props
 */
function LobbyPage({gameId, userId, playerName, gameState: initialGameState, onStart, onLeave, isCreator, isAdmin, onAdmin}) {
    const {toast} = useContext(Ctx);
    const [gameState, setGameState] = useState(initialGameState);
    const [copied, setCopied]       = useState(false);
    const [botCount, setBotCount]   = useState(1);
    const [addingBots, setAddingBots] = useState(false);

    // Stable color map: playerId → colorIndex. Never shrinks so departing players
    // don't cause remaining players to inherit a different color.
    const colorMapRef  = useRef(new Map());
    const colorNextRef = useRef(0);

    const getPlayerColor = (playerId) => {
        if (!colorMapRef.current.has(playerId)) {
            colorMapRef.current.set(playerId, colorNextRef.current++);
        }
        return COLORS[colorMapRef.current.get(playerId) % COLORS.length];
    };

    useEffect(() => {
        if (initialGameState) { setGameState(initialGameState); }
    }, [initialGameState]);

    useEffect(() => {
        const unsub = gameHub.on('GameStateUpdated', state => {
            if (state.gameId === gameId) { setGameState(state); }
        });
        return unsub;
    }, [gameId]);

    const players = gameState?.players || [];
    const hostId  = gameState?.hostId;

    // Seed the color map with current players so colors are assigned in join order.
    players.forEach(p => getPlayerColor(p.id));

    // Host check uses objectId (B2C persistent identity), not name.
    const isHost = isCreator || userId === hostId;

    const handleCopy = () => {
        navigator.clipboard.writeText(gameId).then(() => {
            setCopied(true);
            setTimeout(() => setCopied(false), 2000);
        });
    };

    const handleKick = (player) => {
        if (!confirm(`Kick ${player.name}?`)) { return; }
        gameHub.call('KickPlayer', gameId, player.id)
            .catch(e => toast(e.message || 'Failed to kick player', 'error'));
    };

    const handleRemoveBot = (player) => {
        gameHub.call('RemoveBot', gameId, player.id)
            .catch(e => toast(e.message || 'Failed to remove bot', 'error'));
    };

    const handleAddBots = () => {
        const available = 8 - players.length;
        if (available <= 0) { toast('Lobby is full', 'error'); return; }
        setAddingBots(true);
        gameHub.call('AddBots', gameId, Math.min(botCount, available))
            .then(() => toast(`${Math.min(botCount, available)} bot(s) added`, 'success'))
            .catch(e => toast(e.message || 'Failed to add bots', 'error'))
            .finally(() => setAddingBots(false));
    };

    return (
        <div className="page-enter">
            <Header page="lobby" gameId={gameId} me={playerName} isAdmin={isAdmin} onAdmin={onAdmin} onLeave={onLeave}/>
            <div className="lobby-grid">
                <div>
                    <h2 style={{fontSize: 22, fontWeight: 900, marginBottom: 4}}>Game Lobby</h2>
                    <p style={{color: '#999', fontSize: 13, marginBottom: 18}}>Share the Game ID to invite friends.</p>

                    <div className="gid-box">
                        <div>
                            <div style={{fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: 1, color: '#bbb', marginBottom: 3}}>
                                Game ID
                            </div>
                            <div className="gid-code">{gameId}</div>
                        </div>
                        <button className="btn btn-ghost btn-sm" style={{marginLeft: 'auto'}} onClick={handleCopy}>
                            {copied ? '✓ Copied' : '⎘ Copy'}
                        </button>
                    </div>

                    <div className="slabel">Players ({players.length}/8)</div>
                    <div className="plist">
                        {players.map((player) => (
                            <div key={player.id} className="pitem">
                                <div className="pav" style={{background: getPlayerColor(player.id), color: '#fff'}}>
                                    {player.name[0].toUpperCase()}
                                </div>
                                <div style={{flex: 1}}>
                                    <div style={{fontWeight: 600, fontSize: 13}}>{player.name}</div>
                                    <div style={{fontSize: 11, color: '#aaa'}}>
                                        {player.isBot ? '🤖 Bot' : player.isConnected ? 'Connected' : 'Disconnected'}
                                    </div>
                                </div>
                                {player.id === hostId && <span className="badge bg-yellow">👑 Host</span>}
                                {player.id === userId  && <span className="badge bg-blue">You</span>}
                                {isHost && player.isBot && (
                                    <button
                                        className="btn btn-ghost btn-sm"
                                        style={{fontSize: 11, padding: '2px 8px', color: '#e55', borderColor: '#e55'}}
                                        onClick={() => handleRemoveBot(player)}
                                    >
                                        Remove
                                    </button>
                                )}
                                {isHost && !player.isBot && player.id !== hostId && (
                                    <button
                                        className="btn btn-ghost btn-sm"
                                        style={{fontSize: 11, padding: '2px 8px', color: '#e55', borderColor: '#e55'}}
                                        onClick={() => handleKick(player)}
                                    >
                                        Kick
                                    </button>
                                )}
                            </div>
                        ))}
                        {players.length < 8 && (
                            <div className="pitem" style={{opacity: 0.3, borderStyle: 'dashed'}}>
                                <div className="pav" style={{background: '#eee', color: '#ccc'}}>+</div>
                                <div style={{color: '#ccc', fontSize: 13}}>Waiting for player…</div>
                            </div>
                        )}
                    </div>
                </div>

                <div>
                    <div className="card" style={{marginBottom: 11}}>
                        <div style={{fontSize: 13, fontWeight: 700, marginBottom: 11}}>Game Settings</div>
                        {[
                            ['Min players', '2'],
                            ['Max players', '8'],
                            ['Starting cash', '$1,500'],
                            ['Turn timer', '90s'],
                        ].map(([label, value]) => (
                            <div key={label} style={{display: 'flex', justifyContent: 'space-between', fontSize: 13, marginBottom: 7}}>
                                <span style={{color: '#999'}}>{label}</span>
                                <strong>{value}</strong>
                            </div>
                        ))}
                    </div>

                    {isHost && players.length < 8 && (
                        <div className="card" style={{marginBottom: 11}}>
                            <div style={{fontSize: 12, fontWeight: 700, marginBottom: 10, color: '#777'}}>🤖 Add Bots</div>
                            <div style={{display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10}}>
                                <button className="btn btn-ghost btn-sm" style={{padding: '3px 10px', fontSize: 15}}
                                        onClick={() => setBotCount(c => Math.max(1, c - 1))}>−
                                </button>
                                <span style={{fontSize: 13, fontWeight: 700, flex: 1, textAlign: 'center'}}>
                                    {botCount} bot{botCount !== 1 ? 's' : ''}
                                </span>
                                <button className="btn btn-ghost btn-sm" style={{padding: '3px 10px', fontSize: 15}}
                                        onClick={() => setBotCount(c => Math.min(8 - players.length, c + 1))}>+
                                </button>
                            </div>
                            <button
                                className="btn btn-ghost btn-full btn-sm"
                                onClick={handleAddBots}
                                disabled={addingBots}
                            >
                                {addingBots
                                    ? <span style={{display: 'flex', alignItems: 'center', gap: 6}}><div className="spin"/>Adding…</span>
                                    : '+ Add Bots'
                                }
                            </button>
                        </div>
                    )}

                    {isHost ? (
                        <button
                            className="btn btn-green btn-full btn-lg"
                            onClick={onStart}
                            disabled={players.length < 2}
                        >
                            {players.length < 2
                                ? `Need ${2 - players.length} more player${players.length === 1 ? '' : 's'}`
                                : '▶ Start Game'
                            }
                        </button>
                    ) : (
                        <div style={{textAlign: 'center', padding: 16, border: '1.5px solid var(--border)', borderRadius: 12, background: '#fff'}}>
                            <div className="spin" style={{margin: '0 auto 9px'}}/>
                            <div style={{fontSize: 13, color: '#aaa'}}>Waiting for host to start…</div>
                        </div>
                    )}

                    <button className="btn btn-ghost btn-full" style={{marginTop: 9}} onClick={onLeave}>← Leave</button>
                </div>
            </div>
        </div>
    );
}