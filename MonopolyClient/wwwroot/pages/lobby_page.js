/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */

// components/lobby_page.js — depends on constants.js, header.js.

/**
 * Pre-game lobby showing players, settings, and the start button.
 * @param {{ gameId: string, playerName: string, gameState: any, onStart: function, onLeave: function, isAdmin: boolean, onAdmin: function }} props
 */
function LobbyPage({gameId, playerName, gameState, onStart, onLeave, isAdmin, onAdmin}) {
    const [copied, setCopied] = useState(false);

    const players = gameState?.players || [];
    const hostId = gameState?.hostId;
    const me = players.find(p => p.name === playerName);
    const isHost = me && (me.id === hostId || players[0]?.name === playerName);

    const handleCopy = () => {
        navigator.clipboard.writeText(gameId).then(() => {
            setCopied(true);
            setTimeout(() => setCopied(false), 2000);
        });
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
                            <div style={{
                                fontSize: 10,
                                fontWeight: 700,
                                textTransform: 'uppercase',
                                letterSpacing: 1,
                                color: '#bbb',
                                marginBottom: 3
                            }}>
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
                        {players.map((player, i) => (
                            <div key={player.id} className="pitem">
                                <div className="pav" style={{background: COLORS[i % COLORS.length], color: '#fff'}}>
                                    {player.name[0].toUpperCase()}
                                </div>
                                <div style={{flex: 1}}>
                                    <div style={{fontWeight: 600, fontSize: 13}}>{player.name}</div>
                                    <div style={{fontSize: 11, color: '#aaa'}}>
                                        {player.isConnected ? 'Connected' : 'Disconnected'}
                                    </div>
                                </div>
                                {player.id === hostId && <span className="badge bg-yellow">👑 Host</span>}
                                {player.name === playerName && player.id !== hostId && (
                                    <span className="badge bg-blue">You</span>
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
                            <div key={label} style={{
                                display: 'flex',
                                justifyContent: 'space-between',
                                fontSize: 13,
                                marginBottom: 7
                            }}>
                                <span style={{color: '#999'}}>{label}</span>
                                <strong>{value}</strong>
                            </div>
                        ))}
                    </div>

                    {isHost ? (
                        <button className="btn btn-green btn-full btn-lg" onClick={onStart}
                                disabled={players.length < 2}>
                            {players.length < 2
                                ? `Need ${2 - players.length} more player${players.length === 1 ? '' : 's'}`
                                : '▶ Start Game'
                            }
                        </button>
                    ) : (
                        <div style={{
                            textAlign: 'center',
                            padding: 16,
                            border: '1.5px solid var(--border)',
                            borderRadius: 12,
                            background: '#fff'
                        }}>
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