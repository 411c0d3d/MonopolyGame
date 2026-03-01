/* globals useState, useEffect, useRef, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR, TurnTimer, usePlayerHop, useDiceRoll, DiceTray, ChestCardPopup */

// components/game_page.js — depends on constants.js, signalr.js, header.js, board.js, animations.js.

// Die, DiceTray, ChestCardPopup, usePlayerHop, useDiceRoll are provided by animations.js.

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

    const renderPropertyGroups = (ids) => {
        const groups = groupIdsByColor(ids);
        if (!groups.length) {
            return <span style={{fontSize: 11, color: '#ccc'}}>Nothing</span>;
        }
        return groups.map(({color, items}) => (
            <div key={color}>
                <ColorGroupHeader color={color}/>
                {items.map(({pid, space}) => (
                    <div key={String(pid)} style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 5,
                        fontSize: 12,
                        marginBottom: 3,
                        paddingLeft: 4
                    }}>
                        🏠 <span>{space ? space.name.replace('\n', ' ') : `#${pid}`} <span
                        style={{color: '#bbb', fontSize: 10}}>(#{pid})</span></span>
                    </div>
                ))}
            </div>
        ));
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
                        {renderPropertyGroups(offer.offeredPropertyIds)}
                        {!offer.offeredCash && !(offer.offeredPropertyIds || []).length && (
                            <span style={{fontSize: 11, color: '#ccc'}}>Nothing</span>
                        )}
                    </div>
                    <div className="trade-section">
                        <div className="slabel">They Want</div>
                        {offer.requestedCash > 0 &&
                            <div style={{fontSize: 13, marginBottom: 3}}>💵 ${offer.requestedCash}</div>}
                        {renderPropertyGroups(offer.requestedPropertyIds)}
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
function ProposeTradeModal({target, myProps, board, gameId, turnStartedAt, onClose}) {
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
        const payload = {
            offeredCash: parseInt(offeredCash) || 0,
            requestedCash: parseInt(requestedCash) || 0,
            offeredPropertyIds: offeredProps,
            requestedPropertyIds: requestedProps,
            offeredCardIds: [],
            requestedCardIds: [],
        };
        console.log('[TRADE] Sending ProposeTrade', {
            gameId,
            toPlayerId: target.id,
            toPlayerName: target.name,
            payload
        });
        gameHub.call('ProposeTrade', gameId, target.id, payload)
            .then(() => {
                console.log('[TRADE] ProposeTrade call resolved (hub accepted the invocation)');
                toast('Trade proposal sent!', 'success');
                onClose();
            })
            .catch(e => {
                console.error('[TRADE] ProposeTrade call rejected', e);
                toast(e.message || 'Failed', 'error');
            });
    };

    const renderColorGroupedCheckboxes = (props, selectedList, setter) => {
        if (!props.length) {
            return <div style={{fontSize: 11, color: '#ccc'}}>None</div>;
        }
        const groups = groupPropsByColor(props);
        return groups.map(({color, items}) => (
            <div key={color}>
                <ColorGroupHeader color={color}/>
                {items.map(prop => {
                    const spaceName = prop.space?.name?.replace('\n', ' ') || prop.name;
                    return (
                        <label key={prop.id} style={{
                            display: 'flex', alignItems: 'center', gap: 7,
                            fontSize: 12, marginBottom: 5, cursor: 'pointer', paddingLeft: 4
                        }}>
                            <input
                                type="checkbox"
                                checked={selectedList.includes(prop.id)}
                                onChange={() => toggleProp(selectedList, setter, prop.id)}
                            />
                            <span>
                                {spaceName}
                                <span style={{color: '#bbb', fontSize: 10, marginLeft: 3}}>(#{prop.id})</span>
                            </span>
                        </label>
                    );
                })}
            </div>
        ));
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
                        <div style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            marginBottom: 9
                        }}>
                            <span className="slabel" style={{marginBottom: 0}}>Your Turn</span>
                            <TurnTimer startedAt={turnStartedAt}/>
                        </div>
                        {renderColorGroupedCheckboxes(myProps, offeredProps, setOfferedProps)}
                    </div>
                    <div>
                        <div className="slabel">You Request</div>
                        <div className="ig">
                            <label className="il">Cash ($)</label>
                            <input className="input" type="number" min="0" value={requestedCash}
                                   onChange={e => setRequestedCash(e.target.value)}/>
                        </div>
                        <div className="slabel" style={{marginTop: 7}}>Their Properties</div>
                        {renderColorGroupedCheckboxes(theirProps, requestedProps, setRequestedProps)}
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

                <div style={{background: 'var(--cream)', padding: 16, borderRadius: 10, marginBottom: 16}}>
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
                                        display: 'flex', alignItems: 'center', gap: 8,
                                        padding: '6px 10px', background: '#fff',
                                        border: '1px solid var(--border)', borderRadius: 6, fontSize: 12
                                    }}>
                                        {propSpace?.color && (
                                            <div style={{
                                                width: 8, height: 20, background: BCOLORS[propSpace.color],
                                                borderRadius: 2, flexShrink: 0
                                            }}/>
                                        )}
                                        <span style={{flex: 1, fontWeight: 600}}>{prop.name}</span>
                                        {prop.houseCount > 0 && <span>🏠 {prop.houseCount}</span>}
                                        {prop.hasHotel && <span>🏨</span>}
                                    </div>
                                );
                            })}
                        </div>
                        <div style={{fontSize: 11, color: '#999', marginTop: 8, fontStyle: 'italic'}}>
                            Owning all properties in a color group allows building houses & hotels
                        </div>
                    </div>
                )}

                {!canAfford && (
                    <div style={{
                        background: '#fde8e8', color: 'var(--red)', padding: 10,
                        borderRadius: 8, fontSize: 13, marginBottom: 16, textAlign: 'center', fontWeight: 600
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
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

/**
 * Confirmation modal shown before mortgaging or unmortgaging a property.
 * @param {{ prop: any, space: any, onConfirm: function, onClose: function }} props
 */
function MortgageConfirmModal({prop, space, onConfirm, onClose}) {
    const purchasePrice = space?.purchasePrice || prop?.purchasePrice || 0;
    const mortgageValue = Math.floor(purchasePrice / 2);
    const unmortgageCost = Math.ceil(mortgageValue * 1.1);
    const isMortgaged = prop.isMortgaged;

    return (
        <div className="overlay">
            <div className="mbox" style={{maxWidth: 400}}>
                {space?.color && (
                    <div style={{
                        background: BCOLORS[space.color],
                        height: 36,
                        borderRadius: '8px 8px 0 0',
                        marginBottom: 14
                    }}/>
                )}
                <h3 style={{marginBottom: 4}}>{isMortgaged ? 'Unmortgage' : 'Mortgage'} Property?</h3>
                <div style={{fontWeight: 600, fontSize: 15, marginBottom: 12}}>
                    {prop.name}
                </div>
                <div style={{
                    background: 'var(--cream)', padding: 14, borderRadius: 10, marginBottom: 16,
                    display: 'flex', flexDirection: 'column', gap: 9
                }}>
                    {!isMortgaged ? (
                        <>
                            <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 14}}>
                                <span style={{color: '#666'}}>Purchase Price</span>
                                <strong>${purchasePrice.toLocaleString()}</strong>
                            </div>
                            <div style={{
                                display: 'flex', justifyContent: 'space-between', fontSize: 15,
                                borderTop: '1px solid var(--border)', paddingTop: 9
                            }}>
                                <span style={{color: '#666'}}>You will receive</span>
                                <strong style={{color: 'var(--green)'}}>${mortgageValue.toLocaleString()}</strong>
                            </div>
                            <div style={{fontSize: 11, color: '#999', fontStyle: 'italic'}}>
                                Unmortgaging later costs ${unmortgageCost.toLocaleString()} (10% interest)
                            </div>
                        </>
                    ) : (
                        <>
                            <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 14}}>
                                <span style={{color: '#666'}}>Mortgage Value</span>
                                <strong>${mortgageValue.toLocaleString()}</strong>
                            </div>
                            <div style={{
                                display: 'flex', justifyContent: 'space-between', fontSize: 15,
                                borderTop: '1px solid var(--border)', paddingTop: 9
                            }}>
                                <span style={{color: '#666'}}>Cost to Unmortgage</span>
                                <strong style={{color: 'var(--red)'}}>${unmortgageCost.toLocaleString()}</strong>
                            </div>
                            <div style={{fontSize: 11, color: '#999', fontStyle: 'italic'}}>
                                Includes 10% interest on the ${mortgageValue.toLocaleString()} mortgage value
                            </div>
                        </>
                    )}
                </div>
                <div style={{display: 'flex', gap: 9}}>
                    <button
                        className={`btn btn-full ${isMortgaged ? 'btn-green' : 'btn-red'}`}
                        onClick={() => {
                            onConfirm();
                            onClose();
                        }}
                    >
                        {isMortgaged ? `✓ Unmortgage for $${unmortgageCost.toLocaleString()}` : `✓ Mortgage for $${mortgageValue.toLocaleString()}`}
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

/**
 * Modal for selling houses/hotels back to the bank at 50% build cost.
 * Supports multi-select; shows per-unit value and total payout before confirming.
 * SellHotel downgrades to 4 houses; SellHouse removes one house at a time.
 * @param {{ myProperties: any[], boardSpaces: any[], gameId: string, onClose: function }} props
 */
function LiquidateBuildingsModal({myProperties, boardSpaces, gameId, onClose}) {
    const {toast} = useContext(Ctx);
    const [selected, setSelected] = useState({});
    const [selling, setSelling] = useState(false);

    const buildingProps = myProperties.filter(p => p.houseCount > 0 || p.hasHotel);

    const toggle = id => setSelected(s => ({...s, [id]: !s[id]}));

    /** Returns sell value info for a property based on its current buildings. */
    const getSellInfo = (prop) => {
        const space = SPACES.find(s => s.id === prop.id);
        const houseCost = space?.houseCost || 0;
        const perUnit = Math.floor(houseCost / 2);
        if (prop.hasHotel) {
            return {label: '🏨 Hotel', perUnit, count: 1, total: perUnit};
        }
        return {label: `🏠 × ${prop.houseCount}`, perUnit, count: prop.houseCount, total: perUnit * prop.houseCount};
    };

    const totalPayout = buildingProps
        .filter(p => selected[p.id])
        .reduce((sum, p) => sum + getSellInfo(p).total, 0);

    const handleConfirm = async () => {
        setSelling(true);
        const toSell = buildingProps.filter(p => selected[p.id]);
        try {
            for (const prop of toSell) {
                if (prop.hasHotel) {
                    await gameHub.call('SellHotel', gameId, prop.id);
                } else {
                    for (let i = 0; i < prop.houseCount; i++) {
                        await gameHub.call('SellHouse', gameId, prop.id);
                    }
                }
            }
            toast('Buildings sold!', 'success');
            onClose();
        } catch (e) {
            toast(e.message || 'Failed to sell', 'error');
        } finally {
            setSelling(false);
        }
    };

    return (
        <div className="overlay">
            <div className="mbox" style={{maxWidth: 420}}>
                <h3 style={{marginBottom: 4}}>💰 Liquidate Buildings</h3>
                <p style={{color: '#999', fontSize: 13, marginBottom: 16}}>Select buildings to sell back at 50% build
                    cost</p>

                {buildingProps.length === 0 && (
                    <div style={{color: '#aaa', fontSize: 13, textAlign: 'center', padding: '20px 0'}}>No buildings to
                        sell</div>
                )}

                {buildingProps.map(prop => {
                    const space = SPACES.find(s => s.id === prop.id);
                    const info = getSellInfo(prop);
                    const isSelected = !!selected[prop.id];
                    return (
                        <label key={prop.id} style={{
                            display: 'flex', alignItems: 'center', gap: 10,
                            padding: '8px 10px', borderRadius: 8, marginBottom: 6, cursor: 'pointer',
                            background: isSelected ? 'var(--cream)' : 'transparent',
                            border: `1.5px solid ${isSelected ? 'var(--gold)' : 'var(--border)'}`,
                            transition: 'border-color 0.15s, background 0.15s',
                        }}>
                            <input type="checkbox" checked={isSelected} onChange={() => toggle(prop.id)}/>
                            {space?.color && (
                                <div style={{
                                    width: 8,
                                    height: 20,
                                    background: BCOLORS[space.color] || '#ccc',
                                    borderRadius: 2,
                                    flexShrink: 0
                                }}/>
                            )}
                            <div style={{flex: 1, minWidth: 0}}>
                                <div style={{fontWeight: 600, fontSize: 12}}>{prop.name}</div>
                                <div style={{fontSize: 11, color: '#888'}}>
                                    {info.label}
                                    {info.perUnit > 0 && ` · $${info.perUnit} each`}
                                </div>
                            </div>
                            <div style={{fontSize: 13, fontWeight: 700, color: 'var(--green)', flexShrink: 0}}>
                                +${info.total.toLocaleString()}
                            </div>
                        </label>
                    );
                })}

                {buildingProps.length > 0 && (
                    <div style={{
                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                        padding: '10px 2px', borderTop: '1.5px solid var(--border)', margin: '8px 0 14px',
                    }}>
                        <span style={{color: '#999', fontSize: 13}}>Total payout</span>
                        <strong style={{fontSize: 18, color: 'var(--green)'}}>+${totalPayout.toLocaleString()}</strong>
                    </div>
                )}

                <div style={{display: 'flex', gap: 9}}>
                    <button
                        className="btn btn-green btn-full"
                        onClick={handleConfirm}
                        disabled={!Object.values(selected).some(Boolean) || selling}
                    >
                        {selling ? '…' : '✓ Sell Selected'}
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

/**
 * Main in-game page with board, player panel, action controls, and event log.
 * @param {{ gameId: string, playerName: string, gameState: any, onLeave: function, isAdmin: boolean, onAdmin: function }} props
 */
function GamePage({gameId, playerName, gameState, onLeave, isAdmin, onAdmin, adminKey}) {
    const {toast} = useContext(Ctx);
    const [dice, rolling, triggerRoll, settleDice] = useDiceRoll();
    const [incomingTrade, setIncomingTrade] = useState(null);
    const [jailModalOpen, setJailModalOpen] = useState(false);
    const [paused, setPaused] = useState(gameState?.status === 'Paused');
    const [tradingWith, setTradingWith] = useState(null);
    const [buyModalOpen, setBuyModalOpen] = useState(false);
    const [mortgagePending, setMortgagePending] = useState(null); // { prop, space }
    const [drawnCard, setDrawnCard] = useState(null); // { type, text, amount? }
    const [liquidateOpen, setLiquidateOpen] = useState(false);

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

    useEffect(() => {
        const unsubscribers = [
            gameHub.on('TradeProposed', offer => setIncomingTrade(offer)),
            gameHub.on('GamePaused', () => setPaused(true)),
            gameHub.on('GameResumed', () => setPaused(false)),
            gameHub.on('CardDrawn', card => setDrawnCard(card)),
            gameHub.on('DiceRolled', (d1, d2) => settleDice(d1, d2)),
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
            <Header page="game" me={playerName} isAdmin={isAdmin} onAdmin={onAdmin} onLeave={onLeave}/>
            {paused && <div className="paused-banner">⏸ Game paused by admin</div>}

            {winner && (
                <div style={{
                    position: 'fixed', inset: 0, background: 'rgba(0,0,0,.68)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    zIndex: 600, backdropFilter: 'blur(4px)'
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
                    <Board
                        board={boardSpaces}
                        players={players.filter(p => !p.isBankrupt)}
                        animatedPositions={animatedPositions}
                    />

                    <DiceTray dice={dice} rolling={rolling}/>

                    {isMyTurn && !paused && gameState?.status === 'InProgress' && (
                        <div className="card">
                            <div style={{
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'space-between',
                                marginBottom: 9
                            }}>
                                <span className="slabel" style={{marginBottom: 0}}>Your Turn</span>
                                <TurnTimer startedAt={gameState?.currentTurnStartedAt}/>
                            </div>
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
                            textAlign: 'center', padding: 16, background: '#fff',
                            border: '1.5px solid var(--border)', borderRadius: 12
                        }}>
                            <div className="spin" style={{margin: '0 auto 9px'}}/>
                            <div style={{fontSize: 13, color: '#aaa', marginBottom: 12}}>
                                Waiting for <strong>{currentPlayer?.name}</strong>…
                            </div>
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
                                        width: 8, height: 8, borderRadius: 2,
                                        background: BCOLORS[color] || '#ccc', flexShrink: 0
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
                                const houseCost = space?.houseCost;
                                return (
                                    <div key={prop.id} className="prop-row">
                                        <div style={{display: 'flex', alignItems: 'center', gap: 6, marginBottom: 5}}>
                                            {space?.color && (
                                                <div className="prop-dot"
                                                     style={{background: BCOLORS[space.color] || '#ccc'}}/>
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
                                                        +🏠{houseCost ? ` $${houseCost}` : ''}
                                                    </button>
                                                )}
                                                {space?.type === 'Street' && !prop.isMortgaged && prop.houseCount === 4 && !prop.hasHotel && (
                                                    <button className="btn btn-sm btn-ghost" style={{fontSize: 10}}
                                                            onClick={() => hubCall('BuildHotel', gameId, prop.id)}>
                                                        +🏨{houseCost ? ` $${houseCost}` : ''}
                                                    </button>
                                                )}
                                                <button
                                                    className="btn btn-sm btn-ghost"
                                                    style={{
                                                        fontSize: 10,
                                                        color: prop.isMortgaged ? 'var(--green)' : 'var(--red)'
                                                    }}
                                                    onClick={() => setMortgagePending({prop, space})}
                                                >
                                                    {prop.isMortgaged ? 'Unmortgage' : 'Mortgage'}
                                                </button>
                                            </div>
                                        )}
                                    </div>
                                );
                            })}
                        </div>
                    ))}

                    {myProperties.some(p => p.houseCount > 0 || p.hasHotel) && (
                        <button
                            className="btn btn-ghost btn-sm btn-full"
                            style={{color: 'var(--gold)', marginBottom: 6}}
                            onClick={() => setLiquidateOpen(true)}
                        >
                            💰 Liquidate Buildings
                        </button>
                    )}

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
                                    width: 17, height: 17, borderRadius: '50%',
                                    background: COLORS[idx % COLORS.length],
                                    color: '#fff', fontSize: 8,
                                    display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 700
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

            {liquidateOpen && (
                <LiquidateBuildingsModal
                    myProperties={myProperties}
                    boardSpaces={boardSpaces}
                    gameId={gameId}
                    onClose={() => setLiquidateOpen(false)}
                />
            )}
            {drawnCard && (
                <ChestCardPopup card={drawnCard} onDismiss={() => setDrawnCard(null)}/>
            )}
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
                    onConfirm={() => hubCall('ToggleMortgage', gameId, mortgagePending.prop.id)}
                    onClose={() => setMortgagePending(null)}
                />
            )}
        </div>
    );
}


/** Groups an array of property objects (with id, name) by their color from SPACES. */
const groupPropsByColor = (props) => {
    const groups = {};
    const order = [];
    props.forEach(prop => {
        const space = SPACES.find(s => s.id === prop.id);
        const key = space?.color || '__other';
        if (!groups[key]) {
            groups[key] = [];
            order.push(key);
        }
        groups[key].push({...prop, space});
    });
    return order.map(key => ({color: key, items: groups[key]}));
};

/** Groups an array of property IDs by their color from SPACES. */
const groupIdsByColor = (ids) => {
    const groups = {};
    const order = [];
    (ids || []).forEach(pid => {
        const space = SPACES.find(s => s.id === pid);
        const key = space?.color || '__other';
        if (!groups[key]) {
            groups[key] = [];
            order.push(key);
        }
        groups[key].push({pid, space});
    });
    return order.map(key => ({color: key, items: groups[key]}));
};

/** Color swatch + label for a color group header in trade modals. */
const ColorGroupHeader = ({color}) => (
    <div style={{display: 'flex', alignItems: 'center', gap: 5, marginTop: 8, marginBottom: 3}}>
        {color !== '__other' && (
            <div style={{
                width: 10, height: 10, borderRadius: 2,
                background: BCOLORS[color] || '#ccc', flexShrink: 0
            }}/>
        )}
        <span style={{fontSize: 10, fontWeight: 700, color: '#888', textTransform: 'capitalize'}}>
            {color === '__other' ? 'Other' : color}
        </span>
    </div>
);