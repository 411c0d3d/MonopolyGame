/* globals useState, useEffect, useRef, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR, TurnTimer, usePlayerHop, useDiceRoll, DiceTray */

// components/game_page.js — depends on constants.js, signalr.js, header.js, board.js, animations.js, modals.js.

// Die, DiceTray, ChestCardPopup, usePlayerHop, useDiceRoll are provided by animations.js.
// All modal components and groupPropsByColor/groupIdsByColor/ColorGroupHeader are provided by modals.js.

/**
 * Main in-game page with board, player panel, action controls, and event log.
 * @param {{ gameId: string, playerName: string, gameState: any, onLeave: function, isAdmin: boolean, onAdmin: function }} props
 */
function GamePage({gameId, playerName, gameState, onLeave, isAdmin, onAdmin}) {
    const {toast} = useContext(Ctx);
    const [dice, rolling, triggerRoll, settleDice] = useDiceRoll();
    const [incomingTrade, setIncomingTrade] = useState(null);
    const [jailModalOpen, setJailModalOpen] = useState(false);
    const [paused, setPaused] = useState(gameState?.status === 'Paused');
    const [tradingWith, setTradingWith] = useState(null);
    const [buyModalOpen, setBuyModalOpen] = useState(false);
    const [mortgagePending, setMortgagePending] = useState(null); // { prop, space }
    const [drawnCard, setDrawnCard] = useState(null); // { type, text, amount? }
    const cardTimerRef = React.useRef(null);

    const showCard = useCallback((card) => {
        clearTimeout(cardTimerRef.current);
        setDrawnCard(card);
        cardTimerRef.current = setTimeout(() => setDrawnCard(null), 3500);
    }, []);

    const dismissCard = useCallback(() => {
        clearTimeout(cardTimerRef.current);
        setDrawnCard(null);
    }, []);
    const [liquidateOpen, setLiquidateOpen] = useState(false);
    const [logExpanded, setLogExpanded] = useState(false);
    const [buildPending, setBuildPending] = useState(null); // { prop, space, buildType }
    const [resignOpen, setResignOpen] = useState(false);

    const players = gameState?.players || [];
    const animatedPositions = usePlayerHop(players);
    const boardSpaces = gameState?.board || [];
    const eventLog = gameState?.eventLog || [];
    const currentPlayer = gameState?.currentPlayer;
    const isMyTurn = currentPlayer?.name === playerName;
    const me = players.find(p => p.name === playerName);
    const isHost = !!(me && me.id === gameState?.hostId);
    const myProperties = boardSpaces.filter(b => b.ownerId === me?.id);
    const currentSpace = me ? SPACES[me.position] : null;
    const boardSpace = boardSpaces.find(b => b.id === me?.position);
    const isOwned = !!boardSpace?.ownerId;
    const isMine = boardSpace?.ownerId === me?.id;
    const canBuy = ['Street', 'Railroad', 'Utility'].includes(currentSpace?.type) && !isOwned;
    const winner = gameState?.status === 'Finished' ? players.find(p => !p.isBankrupt) : null;

    /** Component logic for SignalR game event listeners. */
    /** Attaches SignalR listeners for real-time game updates. */
    useEffect(() => {
        const unsubscribers = [
            gameHub.on('TradeProposed', offer => setIncomingTrade(offer)),
            gameHub.on('GamePaused', () => setPaused(true)),
            gameHub.on('GameResumed', () => setPaused(false)),
            gameHub.on('CardDrawn', card => showCard(card)),
            gameHub.on('DiceRolled', (d1, d2) => settleDice([d1, d2])),
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
            gameHub.on('PlayerKicked', ({playerName: kickedName}) => {
                toast(`${kickedName} was removed from the game`, 'info');
            }),
            gameHub.on('TurnWarning', ({message}) => {
                toast(`⏰ ${message}`, 'warning');
            }),
        ];
        return () => unsubscribers.forEach(fn => fn());
    }, [gameId, playerName]);

    const hubCall = (method, ...args) => {
        gameHub.call(method, ...args).catch(e => toast(e.message || 'Action failed', 'error'));
    };

    /** Triggers dice animation then calls the hub. */
    const handleRoll = () => {
        triggerRoll();
        hubCall('RollDice', gameId);
    };

    /** Groups myProperties by color for the right panel. */
    const myPropertyGroups = groupPropsByColor(myProperties);

    return (
        <div className="page-enter">
            <Header page="game" me={playerName} isAdmin={isAdmin} onAdmin={onAdmin} onLeave={onLeave} onBack={onLeave}/>
            {paused && <div className="paused-banner">⏸ Game paused by admin</div>}

            {winner && (
                <div className="winner-overlay">
                    <div className="mbox" style={{textAlign: 'center'}}>
                        <div style={{fontSize: 50, marginBottom: 11}}>🏆</div>
                        <h2 style={{fontSize: 24, marginBottom: 7}}>{winner.name} Wins!</h2>
                        <p style={{color: '#999', marginBottom: 20}}>Last player standing!</p>
                        <button className="btn btn-ink btn-full" onClick={onLeave}>← Back to Home</button>
                    </div>
                </div>
            )}

            <div className="glayout">
                {/* LEFT: Players + Info + Trade */}
                <div className="gsl">
                    <div className="slabel">Players</div>
                    {players.map((player, i) => (
                        <div
                            key={player.id}
                            className={`gpcard${player.id === currentPlayer?.id ? ' active' : ''}${player.isBankrupt ? ' bk' : ''}`}
                        >
                            <div className="pav" style={{
                                background: COLORS[i % COLORS.length],
                                color: '#fff', width: 28, height: 28, fontSize: 11
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
                                    {player.isBot && <span style={{fontSize: 9, color: '#aaa'}}>🤖</span>}
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
                    <div className="info-box">
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
                                <span style={{color: 'rgba(255,255,255,0.45)'}}>{label}</span>
                                <strong style={{color: 'var(--gold)'}}>{value}</strong>
                            </div>
                        ))}
                    </div>
                    <div className="div">Trade</div>
                    <div className="slabel">Trade With</div>
                    {players.filter(p => p.name !== playerName && !p.isBankrupt).map(player => {
                        const idx = players.indexOf(player);
                        return (
                            <button key={player.id} className="btn btn-ghost btn-sm btn-full"
                                    style={{justifyContent: 'flex-start', gap: 7, marginBottom: 5}}
                                    onClick={() => setTradingWith(player)}>
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

                    {/* Event Log — collapsible inline */}
                    <div style={{marginTop: 4}}>
                        <button
                            className="btn btn-ghost btn-sm btn-full"
                            style={{justifyContent: 'space-between', color: 'rgba(255,255,255,0.5)'}}
                            onClick={() => setLogExpanded(v => !v)}
                        >
                            <span>📋 Event Log</span>
                            <span style={{fontSize: 10}}>{logExpanded ? '▲' : '▼'}</span>
                        </button>
                        {logExpanded && (
                            <div style={{
                                marginTop: 6,
                                display: 'flex',
                                flexDirection: 'column',
                                gap: 4,
                                maxHeight: 220,
                                overflowY: 'auto'
                            }}>
                                {[...eventLog].reverse().map((entry, i) => (
                                    <div key={i} className="eitem">{entry}</div>
                                ))}
                                {eventLog.length === 0 && (
                                    <div className="eitem" style={{color: '#ccc'}}>No events yet.</div>
                                )}
                            </div>
                        )}
                    </div>
                </div>

                {/* CENTER: Board with inline action panel */}
                <div className="gmain">
                    <Board
                        board={boardSpaces}
                        players={players.filter(p => !p.isBankrupt)}
                        animatedPositions={animatedPositions}
                        dice={dice}
                        rolling={rolling}
                        actionPanel={
                            <div className="board-actions">
                                {/* Slots 1+2: shared grid — jail spans all 3 cols so its width exactly matches the action row */}
                                <div className="action-grid">
                                    <button className="btn btn-red btn-board"
                                            style={{
                                                gridColumn: '1 / -1',
                                                visibility: (isMyTurn && !paused && me?.isInJail) ? 'visible' : 'hidden',
                                                pointerEvents: (isMyTurn && !paused && me?.isInJail) ? 'auto' : 'none',
                                            }}
                                            onClick={() => setJailModalOpen(true)}>
                                        ⛓ Handle Jail
                                    </button>
                                    <button className="btn btn-gold btn-board" onClick={handleRoll}
                                            disabled={me?.hasRolledDice || rolling || me?.isInJail}
                                            style={{
                                                visibility: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'visible' : 'hidden',
                                                pointerEvents: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'auto' : 'none',
                                            }}>
                                        {rolling ? '…' : '🎲 Roll'}
                                    </button>
                                    <button className="btn btn-green btn-board" onClick={() => setBuyModalOpen(true)}
                                            disabled={!canBuy}
                                            style={{
                                                visibility: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'visible' : 'hidden',
                                                pointerEvents: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'auto' : 'none',
                                            }}>
                                        🏠 {canBuy ? `$${boardSpace?.purchasePrice || '?'}` : 'Buy'}
                                    </button>
                                    <button className="btn btn-ghost btn-board"
                                            onClick={() => hubCall('EndTurn', gameId)}
                                            disabled={!me?.hasRolledDice}
                                            style={{
                                                visibility: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'visible' : 'hidden',
                                                pointerEvents: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'auto' : 'none',
                                            }}>
                                        ⏭ End
                                    </button>
                                </div>

                                {/* Slot 3: status line — location when my turn, waiting spinner otherwise */}
                                <div style={{
                                    fontSize: 'clamp(7px, 1.1cqw, 10px)',
                                    color: '#666',
                                    textAlign: 'center',
                                    lineHeight: 1.3,
                                    minHeight: 'clamp(12px, 2cqw, 16px)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    gap: 5,
                                }}>
                                    {isMyTurn && !paused && currentSpace && gameState?.status === 'InProgress' ? (
                                        <>
                                            📍 <strong>{currentSpace.name.replace('\n', ' ')}</strong>
                                            {canBuy && <span style={{color: 'var(--green)'}}> · available</span>}
                                            {isOwned && !isMine &&
                                                <span style={{color: 'var(--red)'}}> · pay rent</span>}
                                            {isMine && <span style={{color: '#aaa'}}> · yours</span>}
                                        </>
                                    ) : !isMyTurn && gameState?.status === 'InProgress' ? (
                                        <>
                                            <div className="spin" style={{width: 10, height: 10, flexShrink: 0}}/>
                                            <span
                                                style={{color: '#aaa'}}>Waiting for <strong>{currentPlayer?.name}</strong>…</span>
                                        </>
                                    ) : (
                                        <span style={{visibility: 'hidden'}}>—</span>
                                    )}
                                </div>

                                {/* Slot 4: resign + timer — always in flow */}
                                <div style={{
                                    display: 'flex',
                                    flexDirection: 'column',
                                    alignItems: 'center',
                                    gap: 5,
                                    width: '100%',
                                    visibility: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'visible' : 'hidden',
                                    pointerEvents: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'auto' : 'none',
                                }}>
                                    <button className="btn btn-board btn-ghost" style={{color: 'var(--red)'}}
                                            onClick={() => setResignOpen(true)}>
                                        🏳 Resign
                                    </button>
                                    <TurnTimer startedAt={gameState?.currentTurnStartedAt}/>
                                </div>
                            </div>
                        }
                    />
                </div>

                {/* PROPERTIES */}
                <div className="gsr" style={{display: 'flex', flexDirection: 'column', minHeight: 0}}>
                    <div className="slabel" style={{flexShrink: 0}}>Your Properties ({myProperties.length})</div>
                    <div style={{flex: 1, overflowY: 'auto', minHeight: 0}}>
                        {myProperties.length === 0 && (
                            <div style={{fontSize: 11, color: '#ccc'}}>No properties yet.</div>
                        )}
                        {myPropertyGroups.map(({color, items}) => (
                            <div key={color}>
                                {color !== '__other' && (
                                    <div style={{
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: 5,
                                        marginTop: 6,
                                        marginBottom: 3
                                    }}>
                                        <div style={{
                                            width: 8,
                                            height: 8,
                                            borderRadius: 2,
                                            background: BCOLORS[color] || '#ccc',
                                            flexShrink: 0
                                        }}/>
                                        <span style={{
                                            fontSize: 10,
                                            fontWeight: 700,
                                            color: '#888',
                                            textTransform: 'capitalize'
                                        }}>{color}</span>
                                    </div>
                                )}
                                {items.map(prop => {
                                    const space = SPACES.find(s => s.id === prop.id);
                                    const houseCost = prop.houseCost || space?.houseCost;
                                    return (
                                        <div key={prop.id} className="prop-row">
                                            <div style={{
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: 6,
                                                marginBottom: 5
                                            }}>
                                                {space?.color && (
                                                    <div className="prop-dot"
                                                         style={{background: BCOLORS[space.color] || '#ccc'}}/>
                                                )}
                                                <span
                                                    style={{fontSize: 11, fontWeight: 600, flex: 1}}>{prop.name}</span>
                                                {prop.isMortgaged &&
                                                    <span className="badge bg-red" style={{fontSize: 9}}>Mort.</span>}
                                            </div>
                                            <div style={{fontSize: 10, color: '#aaa', marginBottom: 6}}>
                                                {prop.hasHotel ? '🏨 Hotel' : prop.houseCount > 0 ? `🏠 ${prop.houseCount} house${prop.houseCount !== 1 ? 's' : ''}` : 'No buildings'}
                                            </div>
                                            {isMyTurn && !paused && (
                                                <div style={{display: 'flex', gap: 5, flexWrap: 'wrap'}}>
                                                    {space?.type === 'Street' && !prop.isMortgaged && prop.houseCount < 4 && !prop.hasHotel && (
                                                        <button className="btn btn-sm btn-ghost" style={{fontSize: 10}}
                                                                onClick={() => setBuildPending({
                                                                    prop,
                                                                    space,
                                                                    buildType: 'house'
                                                                })}>
                                                            +🏠{houseCost ? ` $${houseCost}` : ''}
                                                        </button>
                                                    )}
                                                    {space?.type === 'Street' && !prop.isMortgaged && prop.houseCount === 4 && !prop.hasHotel && (
                                                        <button className="btn btn-sm btn-ghost" style={{fontSize: 10}}
                                                                onClick={() => setBuildPending({
                                                                    prop,
                                                                    space,
                                                                    buildType: 'hotel'
                                                                })}>
                                                            +🏨{houseCost ? ` $${houseCost}` : ''}
                                                        </button>
                                                    )}
                                                    <button className="btn btn-sm btn-ghost"
                                                            style={{
                                                                fontSize: 10,
                                                                color: prop.isMortgaged ? 'var(--green)' : 'var(--red)'
                                                            }}
                                                            onClick={() => setMortgagePending({prop, space})}>
                                                        {prop.isMortgaged ? 'Unmortgage' : 'Mortgage'}
                                                    </button>
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        ))}
                    </div>
                    <div style={{flexShrink: 0, borderTop: '1px solid var(--border)', paddingTop: 6}}>
                        <button
                            className="btn btn-ghost btn-sm btn-full"
                            style={{
                                color: myProperties.some(p => p.houseCount > 0 || p.hasHotel) ? 'var(--gold)' : 'rgba(255,255,255,0.2)',
                                marginBottom: 6
                            }}
                            disabled={!myProperties.some(p => p.houseCount > 0 || p.hasHotel)}
                            onClick={() => setLiquidateOpen(true)}
                        >
                            💰 Liquidate Buildings
                        </button>
                    </div>
                </div>


            </div>

            {liquidateOpen && (
                <LiquidateBuildingsModal
                    myProperties={myProperties}
                    boardSpaces={boardSpaces}
                    me={me}
                    gameId={gameId}
                    onClose={() => setLiquidateOpen(false)}
                />
            )}
            {drawnCard && (
                <CardDrawnModal card={drawnCard} onDismiss={dismissCard}/>
            )}
            {incomingTrade && (
                <IncomingTradeModal offer={incomingTrade} me={me} gameId={gameId} board={boardSpaces}
                                    onDone={() => setIncomingTrade(null)}/>
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
                    me={me}
                    myProps={myProperties}
                    board={boardSpaces}
                    gameId={gameId}
                    turnStartedAt={gameState?.currentTurnStartedAt}
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
            {mortgagePending && (
                <MortgageConfirmModal
                    prop={mortgagePending.prop}
                    space={mortgagePending.space}
                    me={me}
                    onConfirm={() => hubCall('ToggleMortgage', gameId, mortgagePending.prop.id)}
                    onClose={() => setMortgagePending(null)}
                />
            )}
            {buildPending && (
                <BuildConfirmModal
                    prop={buildPending.prop}
                    space={buildPending.space}
                    buildType={buildPending.buildType}
                    me={me}
                    onConfirm={() => hubCall(buildPending.buildType === 'hotel' ? 'BuildHotel' : 'BuildHouse', gameId, buildPending.prop.id)}
                    onClose={() => setBuildPending(null)}
                />
            )}
            {resignOpen && (
                <ResignModal
                    onConfirm={() => hubCall('ResignPlayer', gameId)}
                    onClose={() => setResignOpen(false)}
                />
            )}
        </div>
    );
}