/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */
// components/game_page.js — depends on constants.js, signalr.js, header.js, board.js.

/** Pip grid positions per die face value. */
const DIE_PIPS = {
    1: [[1, 1]],
    2: [[0, 0], [2, 2]],
    3: [[0, 0], [1, 1], [2, 2]],
    4: [[0, 0], [0, 2], [2, 0], [2, 2]],
    5: [[0, 0], [0, 2], [1, 1], [2, 0], [2, 2]],
    6: [[0, 0], [0, 2], [1, 0], [1, 2], [2, 0], [2, 2]],
};

/**
 * Renders a single die face with animated roll state.
 * @param {{ value: number|null, rolling: boolean }} props
 */
function Die({value, rolling}) {
    const grid = Array(9).fill(false);
    if (value) {
        DIE_PIPS[value].forEach(([row, col]) => {
            grid[row * 3 + col] = true;
        });
    }
    return (
        <div className={`die${rolling ? ' roll' : ''}`}>
            {grid.map((active, i) => <div key={i} className={`pip${active ? '' : ' off'}`}/>)}
        </div>
    );
}

/**
 * Modal for resolving a jail situation on the player's turn.
 * @param {{ player: any, onClose: function, onAct: function }} props
 */
function JailModal({player, onClose, onAct}) {
    return (
        <div className="overlay">
            <div className="mbox">
                <h3 style={{marginBottom: 7}}>⛓ You're in Jail</h3>
                <p style={{color: '#999', fontSize: 13, marginBottom: 18}}>Choose how to proceed:</p>
                <div style={{display: 'flex', flexDirection: 'column', gap: 8}}>
                    {player.keptCardCount > 0 && (
                        <button className="btn btn-gold btn-full" onClick={() => {
                            onAct(true, false);
                            onClose();
                        }}>
                            🃏 Use Get Out of Jail Free Card
                        </button>
                    )}
                    <button className="btn btn-ghost btn-full" onClick={() => {
                        onAct(false, true);
                        onClose();
                    }}>
                        💵 Pay $50 Fine
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={() => {
                        onAct(false, false);
                        onClose();
                    }}>
                        🎲 Try to Roll Doubles
                    </button>
                    <button className="btn btn-sm btn-ghost btn-full" style={{color: '#aaa'}} onClick={onClose}>
                        Cancel
                    </button>
                </div>
            </div>
        </div>
    );
}

/**
 * Modal shown when another player sends a trade proposal.
 * @param {{ offer: any, gameId: string, onDone: function }} props
 */
function IncomingTradeModal({offer, gameId, onDone}) {
    const handleAccept = () => {
        gameHub.call('RespondToTrade', gameId, offer.id, true);
        onDone();
    };
    const handleReject = () => {
        gameHub.call('RespondToTrade', gameId, offer.id, false);
        onDone();
    };

    return (
        <div className="overlay">
            <div className="mbox">
                <h3 style={{marginBottom: 5}}>Trade Proposal</h3>
                <p style={{color: '#999', fontSize: 13, marginBottom: 16}}>
                    <strong>{offer.fromPlayerName}</strong> wants to trade
                </p>
                <div style={{display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 11, marginBottom: 18}}>
                    <div className="trade-section">
                        <div className="slabel">They Offer</div>
                        {offer.offeredCash > 0 &&
                            <div style={{fontSize: 13, marginBottom: 3}}>💵 ${offer.offeredCash}</div>}
                        {(offer.offeredPropertyIds || []).map(pid => (
                            <div key={String(pid)} style={{fontSize: 12, marginBottom: 2}}>🏠 #{pid}</div>
                        ))}
                        {!offer.offeredCash && !(offer.offeredPropertyIds || []).length && (
                            <span style={{fontSize: 11, color: '#ccc'}}>Nothing</span>
                        )}
                    </div>
                    <div className="trade-section">
                        <div className="slabel">They Want</div>
                        {offer.requestedCash > 0 &&
                            <div style={{fontSize: 13, marginBottom: 3}}>💵 ${offer.requestedCash}</div>}
                        {(offer.requestedPropertyIds || []).map(pid => (
                            <div key={String(pid)} style={{fontSize: 12, marginBottom: 2}}>🏠 #{pid}</div>
                        ))}
                        {!offer.requestedCash && !(offer.requestedPropertyIds || []).length && (
                            <span style={{fontSize: 11, color: '#ccc'}}>Nothing</span>
                        )}
                    </div>
                </div>
                <div style={{display: 'flex', gap: 9}}>
                    <button className="btn btn-green btn-full" onClick={handleAccept}>✓ Accept</button>
                    <button className="btn btn-red btn-full" onClick={handleReject}>✕ Decline</button>
                </div>
            </div>
        </div>
    );
}

/**
 * Modal for composing and sending a trade proposal to another player.
 * @param {{ target: any, myProps: any[], board: any[], gameId: string, onClose: function }} props
 */
function ProposeTradeModal({target, myProps, board, gameId, onClose}) {
    const {toast} = useContext(Ctx);
    const [offeredCash, setOfferedCash] = useState('0');
    const [requestedCash, setRequestedCash] = useState('0');
    const [offeredProps, setOfferedProps] = useState([]);
    const [requestedProps, setRequestedProps] = useState([]);

    const theirProps = (board || []).filter(b => b.ownerId === target.id);

    const toggleProp = (currentList, setter, id) => {
        setter(list => list.includes(id) ? list.filter(x => x !== id) : [...list, id]);
    };

    const handleSend = () => {
        gameHub.call('ProposeTrade', gameId, target.id, {
            offeredCash: parseInt(offeredCash) || 0,
            requestedCash: parseInt(requestedCash) || 0,
            offeredPropertyIds: offeredProps,
            requestedPropertyIds: requestedProps,
            offeredCardIds: [],
            requestedCardIds: [],
        })
            .then(() => {
                toast('Trade proposal sent!', 'success');
                onClose();
            })
            .catch(e => toast(e.message || 'Failed', 'error'));
    };

    return (
        <div className="overlay">
            <div className="mbox mbox-wide">
                <h3 style={{marginBottom: 4}}>Propose Trade</h3>
                <p style={{color: '#999', fontSize: 13, marginBottom: 18}}>
                    Trading with <strong>{target.name}</strong>
                </p>
                <div style={{display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 15, marginBottom: 18}}>
                    <div>
                        <div className="slabel">You Offer</div>
                        <div className="ig">
                            <label className="il">Cash ($)</label>
                            <input className="input" type="number" min="0" value={offeredCash}
                                   onChange={e => setOfferedCash(e.target.value)}/>
                        </div>
                        <div className="slabel" style={{marginTop: 7}}>Your Properties</div>
                        {myProps.length === 0 && <div style={{fontSize: 11, color: '#ccc'}}>None</div>}
                        {myProps.map(prop => (
                            <label key={prop.id} style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: 7,
                                fontSize: 12,
                                marginBottom: 5,
                                cursor: 'pointer'
                            }}>
                                <input
                                    type="checkbox"
                                    checked={offeredProps.includes(prop.id)}
                                    onChange={() => toggleProp(offeredProps, setOfferedProps, prop.id)}
                                />
                                {prop.name}
                            </label>
                        ))}
                    </div>
                    <div>
                        <div className="slabel">You Request</div>
                        <div className="ig">
                            <label className="il">Cash ($)</label>
                            <input className="input" type="number" min="0" value={requestedCash}
                                   onChange={e => setRequestedCash(e.target.value)}/>
                        </div>
                        <div className="slabel" style={{marginTop: 7}}>Their Properties</div>
                        {theirProps.length === 0 && <div style={{fontSize: 11, color: '#ccc'}}>None</div>}
                        {theirProps.map(prop => (
                            <label key={prop.id} style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: 7,
                                fontSize: 12,
                                marginBottom: 5,
                                cursor: 'pointer'
                            }}>
                                <input
                                    type="checkbox"
                                    checked={requestedProps.includes(prop.id)}
                                    onChange={() => toggleProp(requestedProps, setRequestedProps, prop.id)}
                                />
                                {prop.name}
                            </label>
                        ))}
                    </div>
                </div>
                <div style={{display: 'flex', gap: 9}}>
                    <button className="btn btn-green btn-full" onClick={handleSend}>📤 Send Proposal</button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

/**
 * Modal for confirming property purchase with balance and related properties shown.
 * @param {{ space: any, boardSpace: any, me: any, myProperties: any[], gameId: string, onClose: function }} props
 */
function BuyPropertyModal({space, boardSpace, me, myProperties, gameId, onClose}) {
    const {toast} = useContext(Ctx);

    const price = boardSpace?.purchasePrice || 0;
    const balanceAfter = me.cash - price;
    const canAfford = me.cash >= price;

    const relatedProperties = myProperties.filter(p => {
        const relatedSpace = SPACES.find(s => s.id === p.id);
        return relatedSpace?.color && relatedSpace.color === space.color;
    });

    const handleConfirm = () => {
        gameHub.call('BuyProperty', gameId)
            .then(() => {
                toast('Property purchased!', 'success');
                onClose();
            })
            .catch(e => toast(e.message || 'Purchase failed', 'error'));
    };

    return (
        <div className="overlay">
            <div className="mbox" style={{maxWidth: 480}}>
                <div style={{marginBottom: 16}}>
                    {space.color && (
                        <div style={{
                            background: BCOLORS[space.color],
                            height: 50,
                            borderRadius: '8px 8px 0 0',
                            marginBottom: 12
                        }}/>
                    )}
                    <h2 style={{fontSize: 22, marginBottom: 6}}>Purchase Property?</h2>
                    <div style={{fontSize: 18, fontWeight: 700, color: 'var(--ink)', marginBottom: 4}}>
                        {space.name.replace('\n', ' ')}
                    </div>
                    <div style={{fontSize: 13, color: '#999'}}>
                        {space.type} • Position #{space.id}
                    </div>
                </div>

                <div style={{
                    background: 'var(--cream)',
                    padding: 16,
                    borderRadius: 10,
                    marginBottom: 16
                }}>
                    <div style={{display: 'flex', justifyContent: 'space-between', marginBottom: 12, fontSize: 15}}>
                        <span style={{color: '#666'}}>Purchase Price</span>
                        <strong style={{fontSize: 18, color: 'var(--gold)'}}>${price.toLocaleString()}</strong>
                    </div>
                    <div style={{display: 'flex', justifyContent: 'space-between', marginBottom: 12, fontSize: 14}}>
                        <span style={{color: '#666'}}>Your Balance</span>
                        <strong style={{color: 'var(--green)'}}>${me.cash.toLocaleString()}</strong>
                    </div>
                    <div style={{
                        borderTop: '1px solid var(--border)',
                        paddingTop: 12,
                        display: 'flex',
                        justifyContent: 'space-between',
                        fontSize: 15
                    }}>
                        <span style={{color: '#666'}}>Balance After Purchase</span>
                        <strong style={{color: canAfford ? 'var(--green)' : 'var(--red)'}}>
                            ${balanceAfter.toLocaleString()}
                        </strong>
                    </div>
                </div>

                {relatedProperties.length > 0 && (
                    <div style={{marginBottom: 16}}>
                        <div className="slabel">Related Properties You Own</div>
                        <div style={{display: 'flex', flexDirection: 'column', gap: 6}}>
                            {relatedProperties.map(prop => {
                                const propSpace = SPACES.find(s => s.id === prop.id);
                                return (
                                    <div key={prop.id} style={{
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: 8,
                                        padding: '6px 10px',
                                        background: '#fff',
                                        border: '1px solid var(--border)',
                                        borderRadius: 6,
                                        fontSize: 12
                                    }}>
                                        {propSpace?.color && (
                                            <div style={{
                                                width: 8,
                                                height: 20,
                                                background: BCOLORS[propSpace.color],
                                                borderRadius: 2,
                                                flexShrink: 0
                                            }}/>
                                        )}
                                        <span style={{flex: 1, fontWeight: 600}}>{prop.name}</span>
                                        {prop.houseCount > 0 && <span>🏠 {prop.houseCount}</span>}
                                        {prop.hasHotel && <span>🏨</span>}
                                    </div>
                                );
                            })}
                        </div>
                        <div style={{
                            fontSize: 11,
                            color: '#999',
                            marginTop: 8,
                            fontStyle: 'italic'
                        }}>
                            Owning all properties in a color group allows building houses & hotels
                        </div>
                    </div>
                )}

                {!canAfford && (
                    <div style={{
                        background: '#fde8e8',
                        color: 'var(--red)',
                        padding: 10,
                        borderRadius: 8,
                        fontSize: 13,
                        marginBottom: 16,
                        textAlign: 'center',
                        fontWeight: 600
                    }}>
                        ⚠ Insufficient funds!
                    </div>
                )}

                <div style={{display: 'flex', gap: 9}}>
                    <button
                        className="btn btn-green btn-full btn-lg"
                        onClick={handleConfirm}
                        disabled={!canAfford}
                    >
                        ✓ Confirm Purchase
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>
                        Cancel
                    </button>
                </div>
            </div>
        </div>
    );
}

/**
 * Main in-game page with board, player panel, action controls, and event log.
 * @param {{ gameId: string, playerName: string, gameState: any, onLeave: function, isAdmin: boolean, onAdmin: function }} props
 */
function GamePage({gameId, playerName, gameState, onLeave, isAdmin, onAdmin}) {
    const {toast} = useContext(Ctx);
    const [dice, setDice] = useState([null, null]);
    const [rolling, setRolling] = useState(false);
    const [incomingTrade, setIncomingTrade] = useState(null);
    const [jailModalOpen, setJailModalOpen] = useState(false);
    const [paused, setPaused] = useState(false);
    const [tradingWith, setTradingWith] = useState(null);
    const [buyModalOpen, setBuyModalOpen] = useState(false);

    const players = gameState?.players || [];
    const boardSpaces = gameState?.board || [];
    const eventLog = gameState?.eventLog || [];
    const currentPlayer = gameState?.currentPlayer;
    const isMyTurn = currentPlayer?.name === playerName;
    const me = players.find(p => p.name === playerName);
    const myProperties = boardSpaces.filter(b => b.ownerId === me?.id);
    const currentSpace = me ? SPACES[me.position] : null;
    const boardSpace = boardSpaces.find(b => b.id === me?.position);
    const isOwned = !!boardSpace?.ownerId;
    const isMine = boardSpace?.ownerId === me?.id;
    const canBuy = ['Street', 'Railroad', 'Utility'].includes(currentSpace?.type) && !isOwned;
    const winner = gameState?.status === 'Finished' ? players.find(p => !p.isBankrupt) : null;

    useEffect(() => {
        const unsubscribers = [
            gameHub.on('TradeProposed', offer => setIncomingTrade(offer)),
            gameHub.on('GamePaused', () => setPaused(true)),
            gameHub.on('GameResumed', () => setPaused(false)),
            gameHub.on('GameForceEnded', () => {
                toast('Game ended by admin', 'error');
                onLeave();
            }),
            gameHub.on('Kicked', msg => {
                toast(msg, 'error');
                onLeave();
            }),
            // Re-join the game group after SignalR auto-reconnect so the server
            // gets the new connectionId, broadcasts GameStateUpdated, and restores
            // board state (including owned properties) for this client.
            gameHub.on('Reconnected', () => {
                gameHub.call('JoinGame', gameId, playerName)
                    .catch(() => toast('Failed to rejoin after reconnect', 'error'));
            }),
            // Notify all players when someone is kicked by an admin.
            gameHub.on('PlayerKicked', playerName => {
                toast(`${playerName} was kicked by the admin`, 'info');
            }),
        ];
        return () => unsubscribers.forEach(fn => fn());
    }, [gameId, playerName]);

    const hubCall = (method, ...args) => {
        gameHub.call(method, ...args).catch(e => toast(e.message || 'Action failed', 'error'));
    };

    const handleRoll = () => {
        setRolling(true);
        const interval = setInterval(
            () => setDice([Math.ceil(Math.random() * 6), Math.ceil(Math.random() * 6)]),
            80,
        );
        setTimeout(() => {
            clearInterval(interval);
            setRolling(false);
        }, 460);
        hubCall('RollDice', gameId);
    };

    return (
        <div className="page-enter">
            <Header page="game" me={playerName} isAdmin={isAdmin} onAdmin={onAdmin} onLeave={onLeave}/>
            {paused && <div className="paused-banner">⏸ Game paused by admin</div>}

            {winner && (
                <div style={{
                    position: 'fixed',
                    inset: 0,
                    background: 'rgba(0,0,0,.68)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    zIndex: 600,
                    backdropFilter: 'blur(4px)'
                }}>
                    <div className="mbox" style={{textAlign: 'center'}}>
                        <div style={{fontSize: 50, marginBottom: 11}}>🏆</div>
                        <h2 style={{fontSize: 24, marginBottom: 7}}>{winner.name} Wins!</h2>
                        <p style={{color: '#999', marginBottom: 20}}>Last player standing!</p>
                        <button className="btn btn-ink btn-full" onClick={onLeave}>← Back to Home</button>
                    </div>
                </div>
            )}

            <div className="glayout">
                {/* LEFT: Players */}
                <div className="gsl">
                    <div className="slabel">Players</div>
                    {players.map((player, i) => (
                        <div
                            key={player.id}
                            className={`gpcard${player.id === currentPlayer?.id ? ' active' : ''}${player.isBankrupt ? ' bk' : ''}`}
                        >
                            <div className="pav" style={{
                                background: COLORS[i % COLORS.length],
                                color: '#fff',
                                width: 28,
                                height: 28,
                                fontSize: 11
                            }}>
                                {player.name[0]}
                            </div>
                            <div style={{flex: 1, minWidth: 0}}>
                                <div style={{
                                    fontWeight: 600,
                                    fontSize: 11,
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: 4
                                }}>
                                    {player.name}
                                    {player.name === playerName &&
                                        <span style={{fontSize: 9, color: '#bbb'}}>(you)</span>}
                                    {player.id === currentPlayer?.id && ' 🎲'}
                                </div>
                                <div className="gcash">${player.cash?.toLocaleString()}</div>
                                <div className="gstat">
                                    #{player.position} · {player.isInJail ? '⛓Jail' : player.isBankrupt ? '💀Out' : player.isConnected ? '🟢' : '🔴Away'}
                                </div>
                            </div>
                        </div>
                    ))}
                    <div className="div">Info</div>
                    <div style={{
                        padding: '10px 13px',
                        background: '#fff',
                        border: '1.5px solid var(--border)',
                        borderRadius: 10
                    }}>
                        {[
                            ['Turn', `#${gameState?.turn || 0}`],
                            ['Current', currentPlayer?.name || '—'],
                            ['Active', players.filter(p => !p.isBankrupt).length],
                        ].map(([label, value]) => (
                            <div key={String(label)} style={{
                                display: 'flex',
                                justifyContent: 'space-between',
                                fontSize: 11,
                                marginBottom: 5
                            }}>
                                <span style={{color: '#aaa'}}>{label}</span>
                                <strong style={{color: 'var(--gold)'}}>{value}</strong>
                            </div>
                        ))}
                    </div>
                </div>

                {/* CENTER: Board + controls */}
                <div className="gmain">
                    <Board board={boardSpaces} players={players.filter(p => !p.isBankrupt)}/>

                    <div className="dice-area">
                        <Die value={dice[0]} rolling={rolling}/>
                        <Die value={dice[1]} rolling={rolling}/>
                        {dice[0] && dice[1] && (
                            <div style={{fontSize: 13, color: '#aaa', marginLeft: 8}}>
                                = {dice[0] + dice[1]}
                                {dice[0] === dice[1] && (
                                    <span style={{color: 'var(--gold)', marginLeft: 6, fontWeight: 700}}>Doubles!</span>
                                )}
                            </div>
                        )}
                    </div>

                    {isMyTurn && !paused && gameState?.status === 'InProgress' && (
                        <div className="card">
                            <div className="slabel" style={{marginBottom: 9}}>Your Turn</div>
                            {me?.isInJail && (
                                <button className="btn btn-red btn-full" style={{marginBottom: 9}}
                                        onClick={() => setJailModalOpen(true)}>
                                    ⛓ Handle Jail Situation
                                </button>
                            )}
                            <div className="action-grid">
                                <button className="btn btn-gold btn-lg" onClick={handleRoll}
                                        disabled={me?.hasRolledDice || rolling || me?.isInJail}>
                                    {rolling ? '…' : '🎲 Roll Dice'}
                                </button>
                                <button className="btn btn-green" onClick={() => setBuyModalOpen(true)}
                                        disabled={!canBuy}>
                                    🏠 Buy {canBuy ? `($${boardSpace?.purchasePrice || '?'})` : 'Property'}
                                </button>
                                <button className="btn btn-ghost" onClick={() => hubCall('DeclineProperty', gameId)}
                                        disabled={!canBuy}>
                                    ✕ Decline
                                </button>
                                <button className="btn btn-ghost" onClick={() => hubCall('EndTurn', gameId)}
                                        disabled={!me?.hasRolledDice}>
                                    ⏭ End Turn
                                </button>
                            </div>
                            {currentSpace && (
                                <div style={{
                                    marginTop: 11,
                                    padding: '8px 11px',
                                    background: 'var(--cream)',
                                    borderRadius: 8,
                                    fontSize: 12
                                }}>
                                    📍 <strong>{currentSpace.name.replace('\n', ' ')}</strong>
                                    {canBuy && <span style={{color: 'var(--green)', marginLeft: 5}}>← available!</span>}
                                    {isOwned && !isMine &&
                                        <span style={{color: 'var(--red)', marginLeft: 5}}>← pay rent!</span>}
                                    {isMine && <span style={{color: '#aaa', marginLeft: 5}}>← yours</span>}
                                </div>
                            )}
                            <button
                                className="btn btn-sm btn-ghost"
                                style={{marginTop: 9, color: 'var(--red)', width: 'auto'}}
                                onClick={() => {
                                    if (confirm('Resign from game?')) {
                                        hubCall('ResignPlayer', gameId);
                                    }
                                }}
                            >
                                🏳 Resign
                            </button>
                        </div>
                    )}

                    {!isMyTurn && gameState?.status === 'InProgress' && (
                        <div style={{
                            textAlign: 'center',
                            padding: 16,
                            background: '#fff',
                            border: '1.5px solid var(--border)',
                            borderRadius: 12
                        }}>
                            <div className="spin" style={{margin: '0 auto 9px'}}/>
                            <div style={{fontSize: 13, color: '#aaa', marginBottom: 12}}>
                                Waiting for <strong>{currentPlayer?.name}</strong>…
                            </div>
                            <button
                                className="btn btn-ghost btn-sm"
                                onClick={() => hubCall('EndTurn', gameId)}
                            >
                                ⏭ End Turn (Skip)
                            </button>
                        </div>
                    )}

                    <div>
                        <div className="slabel">Event Log</div>
                        <div className="elog">
                            {[...eventLog].reverse().map((entry, i) => (
                                <div key={i} className="eitem">{entry}</div>
                            ))}
                            {eventLog.length === 0 && (
                                <div className="eitem" style={{color: '#ccc'}}>No events yet.</div>
                            )}
                        </div>
                    </div>
                </div>

                {/* RIGHT: Properties + Trade */}
                <div className="gsr">
                    <div className="slabel">Your Properties ({myProperties.length})</div>
                    {myProperties.length === 0 && (
                        <div style={{fontSize: 11, color: '#ccc'}}>No properties yet.</div>
                    )}
                    {myProperties.map(prop => {
                        const space = SPACES.find(s => s.id === prop.id);
                        return (
                            <div key={prop.id} className="prop-row">
                                <div style={{display: 'flex', alignItems: 'center', gap: 6, marginBottom: 5}}>
                                    {space?.color && (
                                        <div className="prop-dot" style={{background: BCOLORS[space.color] || '#ccc'}}/>
                                    )}
                                    <span style={{fontSize: 11, fontWeight: 600, flex: 1}}>{prop.name}</span>
                                    {prop.isMortgaged &&
                                        <span className="badge bg-red" style={{fontSize: 9}}>Mort.</span>}
                                </div>
                                <div style={{fontSize: 10, color: '#aaa', marginBottom: 6}}>
                                    {prop.hasHotel
                                        ? '🏨 Hotel'
                                        : prop.houseCount > 0
                                            ? `🏠 ${prop.houseCount} house${prop.houseCount !== 1 ? 's' : ''}`
                                            : 'No buildings'
                                    }
                                </div>
                                {isMyTurn && !paused && (
                                    <div style={{display: 'flex', gap: 5, flexWrap: 'wrap'}}>
                                        {space?.type === 'Street' && !prop.isMortgaged && prop.houseCount < 4 && !prop.hasHotel && (
                                            <button className="btn btn-sm btn-ghost" style={{fontSize: 10}}
                                                    onClick={() => hubCall('BuildHouse', gameId, prop.id)}>
                                                +🏠
                                            </button>
                                        )}
                                        {space?.type === 'Street' && !prop.isMortgaged && prop.houseCount === 4 && !prop.hasHotel && (
                                            <button className="btn btn-sm btn-ghost" style={{fontSize: 10}}
                                                    onClick={() => hubCall('BuildHotel', gameId, prop.id)}>
                                                +🏨
                                            </button>
                                        )}
                                        <button
                                            className="btn btn-sm btn-ghost"
                                            style={{
                                                fontSize: 10,
                                                color: prop.isMortgaged ? 'var(--green)' : 'var(--red)'
                                            }}
                                            onClick={() => hubCall('ToggleMortgage', gameId, prop.id)}
                                        >
                                            {prop.isMortgaged ? 'Unmortgage' : 'Mortgage'}
                                        </button>
                                    </div>
                                )}
                            </div>
                        );
                    })}

                    <div className="div">Trade</div>
                    <div className="slabel">Trade With</div>
                    {players.filter(p => p.name !== playerName && !p.isBankrupt).map(player => {
                        const idx = players.indexOf(player);
                        return (
                            <button
                                key={player.id}
                                className="btn btn-ghost btn-sm btn-full"
                                style={{justifyContent: 'flex-start', gap: 7, marginBottom: 5}}
                                onClick={() => setTradingWith(player)}
                            >
                                <div style={{
                                    width: 17,
                                    height: 17,
                                    borderRadius: '50%',
                                    background: COLORS[idx % COLORS.length],
                                    color: '#fff',
                                    fontSize: 8,
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    fontWeight: 700
                                }}>
                                    {player.name[0]}
                                </div>
                                {player.name}
                            </button>
                        );
                    })}

                    <div className="div"/>
                    <button className="btn btn-ghost btn-sm btn-full" onClick={onLeave}>← Leave Game</button>
                </div>
            </div>

            {incomingTrade && (
                <IncomingTradeModal offer={incomingTrade} gameId={gameId} onDone={() => setIncomingTrade(null)}/>
            )}
            {jailModalOpen && me && (
                <JailModal
                    player={me}
                    onClose={() => setJailModalOpen(false)}
                    onAct={(useCard, payFine) => hubCall('HandleJail', gameId, useCard, payFine)}
                />
            )}
            {tradingWith && (
                <ProposeTradeModal
                    target={tradingWith}
                    myProps={myProperties}
                    board={boardSpaces}
                    gameId={gameId}
                    onClose={() => setTradingWith(null)}
                />
            )}
            {buyModalOpen && canBuy && currentSpace && (
                <BuyPropertyModal
                    space={currentSpace}
                    boardSpace={boardSpace}
                    me={me}
                    myProperties={myProperties}
                    gameId={gameId}
                    onClose={() => setBuyModalOpen(false)}
                />
            )}
        </div>
    );
}