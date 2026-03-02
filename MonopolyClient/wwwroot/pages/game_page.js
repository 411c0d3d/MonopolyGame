/* globals useState, useEffect, useRef, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR, TurnTimer, usePlayerHop, useDiceRoll, DiceTray */

// components/game_page.js — depends on constants.js, signalr.js, header.js, board.js, animations.js.

// Die, DiceTray, ChestCardPopup, usePlayerHop, useDiceRoll are provided by animations.js.

/**
 * Compact balance strip shown at the top of all action modals.
 * @param {{ cash: number, label?: string }} props
 */
function ModalBalanceBar({cash, label = 'Your Balance'}) {
    return (
        <div className="modal-balance-bar">
            <span className="modal-balance-label">{label}</span>
            <strong className="modal-balance-value">${cash?.toLocaleString() ?? '—'}</strong>
        </div>
    );
}

/**
 * Modal for resolving a jail situation on the player's turn.
 * @param {{ player: any, onClose: function, onAct: function }} props
 */
function JailModal({player, onClose, onAct}) {
    const canAffordFine = player.cash >= 50;
    return (
        <div className="overlay">
            <div className="mbox">
                <h3 style={{marginBottom: 7}}>⛓ You're in Jail</h3>
                <ModalBalanceBar cash={player.cash}/>
                <p style={{color: '#999', fontSize: 13, marginBottom: 14}}>Choose how to proceed:</p>
                <div style={{display: 'flex', flexDirection: 'column', gap: 8}}>
                    {player.keptCardCount > 0 && (
                        <button className="btn btn-gold btn-full"
                                onClick={() => {
                                    onAct(true, false);
                                    onClose();
                                }}>
                            🃏 Use Get Out of Jail Free Card
                        </button>
                    )}
                    <button className="btn btn-ghost btn-full" disabled={!canAffordFine}
                            onClick={() => {
                                onAct(false, true);
                                onClose();
                            }}
                            style={{display: 'flex', justifyContent: 'space-between'}}>
                        <span>💵 Pay $50 Fine</span>
                        <span style={{color: canAffordFine ? 'var(--red)' : '#aaa', fontSize: 11}}>
                            → ${(player.cash - 50).toLocaleString()} left
                        </span>
                    </button>
                    <button className="btn btn-ghost btn-full"
                            onClick={() => {
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
 * @param {{ offer: any, me: any, gameId: string, onDone: function }} props
 */
function IncomingTradeModal({offer, me, gameId, board, onDone}) {
    const handleAccept = () => {
        gameHub.call('RespondToTrade', gameId, offer.id, true);
        onDone();
    };
    const handleReject = () => {
        gameHub.call('RespondToTrade', gameId, offer.id, false);
        onDone();
    };

    const netCash = (offer.offeredCash || 0) - (offer.requestedCash || 0);
    const balanceAfter = me ? me.cash + netCash : null;

    /** Renders a property list row with color swatch, name, and price from live board data. */
    const renderPropertyGroups = (ids) => {
        const groups = groupIdsByColor(ids);
        if (!groups.length) {
            return <span style={{fontSize: 11, color: '#aaa'}}>Nothing</span>;
        }
        return groups.map(({color, items}) => (
            <div key={color}>
                <ColorGroupHeader color={color}/>
                {items.map(({pid, space}) => {
                    const boardSpace = board?.find(b => b.id === pid);
                    const price = boardSpace?.purchasePrice || space?.price;
                    return (
                        <div key={String(pid)} style={{
                            display: 'flex', alignItems: 'center', gap: 5,
                            fontSize: 12, marginBottom: 4, paddingLeft: 2,
                        }}>
                            {space?.color
                                ? <div style={{
                                    width: 7,
                                    height: 14,
                                    background: BCOLORS[space.color],
                                    borderRadius: 1,
                                    flexShrink: 0
                                }}/>
                                : <span>🏠</span>
                            }
                            <span style={{flex: 1}}>{space ? space.name.replace('\n', ' ') : `#${pid}`}</span>
                            {price && (
                                <span style={{color: 'var(--gold)', fontWeight: 700, fontSize: 11}}>
                                ${price.toLocaleString()}
                            </span>
                            )}
                        </div>
                    );
                })}
            </div>
        ));
    };
    return (
        <div className="overlay">
            <div className="mbox">
                <h3 style={{marginBottom: 5}}>Trade Proposal</h3>
                <p style={{fontSize: 13, marginBottom: 10}}>
                    <strong>{offer.fromPlayerName}</strong> wants to trade with you
                </p>
                {me && <ModalBalanceBar cash={me.cash}/>}
                <div style={{display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 11, marginBottom: 12}}>
                    <div className="trade-section">
                        <div className="slabel">They Offer</div>
                        {offer.offeredCash > 0 && (
                            <div style={{fontSize: 13, fontWeight: 700, color: 'var(--green)', marginBottom: 5}}>
                                💵 +${offer.offeredCash.toLocaleString()}
                            </div>
                        )}
                        {renderPropertyGroups(offer.offeredPropertyIds)}
                        {!offer.offeredCash && !(offer.offeredPropertyIds || []).length && (
                            <span style={{fontSize: 11, color: '#aaa'}}>Nothing</span>
                        )}
                    </div>
                    <div className="trade-section">
                        <div className="slabel">They Want</div>
                        {offer.requestedCash > 0 && (
                            <div style={{fontSize: 13, fontWeight: 700, color: 'var(--red)', marginBottom: 5}}>
                                💵 −${offer.requestedCash.toLocaleString()}
                            </div>
                        )}
                        {renderPropertyGroups(offer.requestedPropertyIds)}
                        {!offer.requestedCash && !(offer.requestedPropertyIds || []).length && (
                            <span style={{fontSize: 11, color: '#aaa'}}>Nothing</span>
                        )}
                    </div>
                </div>
                {me && (
                    <div style={{
                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                        background: 'var(--cream)', border: '1px solid var(--border)',
                        borderRadius: 8, padding: '7px 12px', marginBottom: 14, fontSize: 13,
                    }}>
                        <span style={{color: '#aaa', fontSize: 11}}>CASH AFTER DEAL</span>
                        <strong style={{color: netCash >= 0 ? 'var(--green)' : 'var(--red)', fontSize: 15}}>
                            ${balanceAfter.toLocaleString()}
                            <span style={{fontSize: 10, fontWeight: 400, marginLeft: 5}}>
                                ({netCash >= 0 ? '+' : ''}{netCash.toLocaleString()})
                            </span>
                        </strong>
                    </div>
                )}
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
 * @param {{ target: any, me: any, myProps: any[], board: any[], gameId: string, turnStartedAt: string, onClose: function }} props
 */
function ProposeTradeModal({target, me, myProps, board, gameId, turnStartedAt, onClose}) {
    const {toast} = useContext(Ctx);
    const [offeredCash, setOfferedCash] = useState('0');
    const [requestedCash, setRequestedCash] = useState('0');
    const [offeredProps, setOfferedProps] = useState([]);
    const [requestedProps, setRequestedProps] = useState([]);

    const theirProps = (board || []).filter(b => b.ownerId === target.id);

    const netCash = (parseInt(requestedCash) || 0) - (parseInt(offeredCash) || 0);
    const balanceAfter = me ? me.cash - (parseInt(offeredCash) || 0) + (parseInt(requestedCash) || 0) : null;

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

    /** Renders color-grouped property checkboxes with price shown per row. */
    const renderColorGroupedCheckboxes = (props, selectedList, setter) => {
        if (!props.length) {
            return <div style={{fontSize: 11, color: '#aaa'}}>None</div>;
        }
        const groups = groupPropsByColor(props);
        return groups.map(({color, items}) => (
            <div key={color}>
                <ColorGroupHeader color={color}/>
                {items.map(prop => {
                    const spaceName = prop.space?.name?.replace('\n', ' ') || prop.name;
                    const price = prop.purchasePrice || prop.space?.price || prop.space?.purchasePrice;
                    return (
                        <label key={prop.id} style={{
                            display: 'flex', alignItems: 'center', gap: 7,
                            fontSize: 12, marginBottom: 5, cursor: 'pointer', paddingLeft: 2,
                        }}>
                            <input
                                type="checkbox"
                                checked={selectedList.includes(prop.id)}
                                onChange={() => toggleProp(selectedList, setter, prop.id)}
                            />
                            {prop.space?.color && (
                                <div style={{
                                    width: 7,
                                    height: 14,
                                    background: BCOLORS[prop.space.color],
                                    borderRadius: 1,
                                    flexShrink: 0
                                }}/>
                            )}
                            <span style={{flex: 1}}>{spaceName}</span>
                            {price && (
                                <span style={{color: 'var(--gold)', fontWeight: 700, fontSize: 11, flexShrink: 0}}>
                                    ${price.toLocaleString()}
                                </span>
                            )}
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
                <p style={{color: '#999', fontSize: 13, marginBottom: 10}}>
                    Trading with <strong>{target.name}</strong>
                </p>
                {me && <ModalBalanceBar cash={me.cash}/>}
                <div style={{display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 15, marginBottom: 14}}>
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
                {me && (
                    <div style={{
                        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                        background: 'var(--cream)', border: '1px solid var(--border)',
                        borderRadius: 8, padding: '7px 12px', marginBottom: 14, fontSize: 13,
                    }}>
                        <span style={{color: '#aaa', fontSize: 11}}>YOUR CASH AFTER DEAL</span>
                        <strong style={{color: balanceAfter >= 0 ? 'var(--gold)' : 'var(--red)', fontSize: 15}}>
                            ${balanceAfter?.toLocaleString()}
                            {netCash !== 0 && (
                                <span style={{fontSize: 10, fontWeight: 400, marginLeft: 5}}>
                                    ({netCash > 0 ? '+' : ''}{netCash.toLocaleString()})
                                </span>
                            )}
                        </strong>
                    </div>
                )}
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
                                        padding: '6px 10px', background: 'rgba(255,255,255,0.06)',
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
                        background: 'rgba(229,57,53,0.12)', color: 'var(--red)', padding: 10,
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
 * @param {{ prop: any, space: any, me: any, onConfirm: function, onClose: function }} props
 */
function MortgageConfirmModal({prop, space, me, onConfirm, onClose}) {
    const purchasePrice = space?.purchasePrice || prop?.purchasePrice || 0;
    const mortgageValue = Math.floor(purchasePrice / 2);
    const unmortgageCost = Math.ceil(mortgageValue * 1.1);
    const isMortgaged = prop.isMortgaged;
    const balanceAfter = me ? (isMortgaged ? me.cash - unmortgageCost : me.cash + mortgageValue) : null;
    const canAfford = !isMortgaged || !me || me.cash >= unmortgageCost;

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
                <div style={{display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12}}>
                    {space?.color && (
                        <div style={{
                            width: 8,
                            height: 18,
                            background: BCOLORS[space.color],
                            borderRadius: 2,
                            flexShrink: 0
                        }}/>
                    )}
                    <span style={{fontWeight: 600, fontSize: 15}}>{prop.name}</span>
                    <span style={{
                        color: '#aaa',
                        fontSize: 12,
                        marginLeft: 'auto'
                    }}>${purchasePrice.toLocaleString()}</span>
                </div>
                {me && <ModalBalanceBar cash={me.cash}/>}
                <div style={{
                    background: 'var(--cream)', padding: 14, borderRadius: 10, marginBottom: 16,
                    display: 'flex', flexDirection: 'column', gap: 9,
                }}>
                    {!isMortgaged ? (
                        <>
                            <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 14}}>
                                <span style={{color: '#aaa'}}>Purchase Price</span>
                                <strong>${purchasePrice.toLocaleString()}</strong>
                            </div>
                            <div style={{
                                display: 'flex',
                                justifyContent: 'space-between',
                                fontSize: 15,
                                borderTop: '1px solid var(--border)',
                                paddingTop: 9
                            }}>
                                <span style={{color: '#aaa'}}>You will receive</span>
                                <strong style={{color: 'var(--green)'}}>+${mortgageValue.toLocaleString()}</strong>
                            </div>
                            {balanceAfter !== null && (
                                <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 13}}>
                                    <span style={{color: '#aaa'}}>Balance after</span>
                                    <strong style={{color: 'var(--gold)'}}>${balanceAfter.toLocaleString()}</strong>
                                </div>
                            )}
                            <div style={{fontSize: 11, color: '#aaa', fontStyle: 'italic'}}>
                                Unmortgaging later costs ${unmortgageCost.toLocaleString()} (10% interest)
                            </div>
                        </>
                    ) : (
                        <>
                            <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 14}}>
                                <span style={{color: '#aaa'}}>Mortgage Value</span>
                                <strong>${mortgageValue.toLocaleString()}</strong>
                            </div>
                            <div style={{
                                display: 'flex',
                                justifyContent: 'space-between',
                                fontSize: 15,
                                borderTop: '1px solid var(--border)',
                                paddingTop: 9
                            }}>
                                <span style={{color: '#aaa'}}>Cost to Unmortgage</span>
                                <strong style={{color: 'var(--red)'}}>−${unmortgageCost.toLocaleString()}</strong>
                            </div>
                            {balanceAfter !== null && (
                                <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 13}}>
                                    <span style={{color: '#aaa'}}>Balance after</span>
                                    <strong style={{color: canAfford ? 'var(--gold)' : 'var(--red)'}}>
                                        ${balanceAfter.toLocaleString()}
                                    </strong>
                                </div>
                            )}
                            <div style={{fontSize: 11, color: '#aaa', fontStyle: 'italic'}}>
                                Includes 10% interest on the ${mortgageValue.toLocaleString()} mortgage value
                            </div>
                        </>
                    )}
                </div>
                {!canAfford && (
                    <div style={{
                        background: 'rgba(229,57,53,0.12)', color: 'var(--red)',
                        padding: 10, borderRadius: 8, fontSize: 13, marginBottom: 12,
                        textAlign: 'center', fontWeight: 600,
                    }}>
                        ⚠ Insufficient funds to unmortgage!
                    </div>
                )}
                <div style={{display: 'flex', gap: 9}}>
                    <button
                        className={`btn btn-full ${isMortgaged ? 'btn-green' : 'btn-red'}`}
                        disabled={!canAfford}
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
 * @param {{ myProperties: any[], boardSpaces: any[], me: any, gameId: string, onClose: function }} props
 */
function LiquidateBuildingsModal({myProperties, boardSpaces, me, gameId, onClose}) {
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

    const balanceAfter = me ? me.cash + totalPayout : null;

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
                <p style={{color: '#999', fontSize: 13, marginBottom: 10}}>
                    Select buildings to sell back at 50% build cost
                </p>
                {me && <ModalBalanceBar cash={me.cash}/>}

                {buildingProps.length === 0 && (
                    <div style={{color: '#aaa', fontSize: 13, textAlign: 'center', padding: '20px 0'}}>
                        No buildings to sell
                    </div>
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
                                <div style={{fontSize: 11, color: '#aaa'}}>
                                    {info.label}
                                    {info.perUnit > 0 && ` · $${info.perUnit} each`}
                                    {space?.houseCost &&
                                        <span style={{color: '#888'}}> (build cost: ${space.houseCost})</span>}
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
                        borderTop: '1.5px solid var(--border)', margin: '8px 0 8px',
                        paddingTop: 10, display: 'flex', flexDirection: 'column', gap: 6,
                    }}>
                        <div style={{display: 'flex', justifyContent: 'space-between', alignItems: 'center'}}>
                            <span style={{color: '#aaa', fontSize: 13}}>Total payout</span>
                            <strong
                                style={{fontSize: 18, color: 'var(--green)'}}>+${totalPayout.toLocaleString()}</strong>
                        </div>
                        {balanceAfter !== null && totalPayout > 0 && (
                            <div style={{display: 'flex', justifyContent: 'space-between', alignItems: 'center'}}>
                                <span style={{color: '#aaa', fontSize: 12}}>Balance after</span>
                                <strong style={{
                                    color: 'var(--gold)',
                                    fontSize: 14
                                }}>${balanceAfter.toLocaleString()}</strong>
                            </div>
                        )}
                    </div>
                )}

                <div style={{display: 'flex', gap: 9, marginTop: 6}}>
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
 * Purely presentational card reveal modal. Timer and dismiss logic live in GamePage.
 * Progress bar is driven by a CSS animation — zero JS ticking, zero state, zero re-renders.
 * @param {{ card: {type: string, text: string, amount?: number}, onDismiss: function }} props
 */
function CardDrawnModal({card, onDismiss}) {
    const isChance = card.type === 'Chance';
    const accentColor = isChance ? '#b8860b' : '#2d6a4f';
    const bgColor = isChance ? '#fff9c4' : '#c8e6c9';
    const icon = isChance ? '❓' : '🏛';

    return (
        <div className="overlay" onClick={onDismiss}>
            <div className="mbox" style={{maxWidth: 380, textAlign: 'center', padding: 0, overflow: 'hidden'}}
                 onClick={e => e.stopPropagation()}>
                <div style={{
                    background: bgColor,
                    padding: 'clamp(14px, 3vw, 22px) 24px 12px',
                    borderBottom: `2px solid ${accentColor}44`,
                }}>
                    <div style={{fontSize: 42, lineHeight: 1, marginBottom: 8}}>{icon}</div>
                    <div style={{
                        fontFamily: 'Playfair Display, serif',
                        fontWeight: 700,
                        fontSize: 13,
                        letterSpacing: '0.12em',
                        textTransform: 'uppercase',
                        color: accentColor,
                    }}>{card.type}</div>
                </div>
                <div style={{padding: '20px 24px 16px', background: 'rgba(14,24,62,0.96)'}}>
                    <p style={{
                        fontSize: 15,
                        fontWeight: 500,
                        color: 'var(--ink)',
                        lineHeight: 1.55,
                        marginBottom: card.amount ? 14 : 0,
                    }}>{card.text}</p>
                    {card.amount != null && card.amount !== 0 && (
                        <div style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: 6,
                            background: card.amount > 0 ? 'rgba(26,158,120,0.15)' : 'rgba(229,57,53,0.15)',
                            border: `1px solid ${card.amount > 0 ? 'rgba(26,158,120,0.3)' : 'rgba(229,57,53,0.3)'}`,
                            borderRadius: 8,
                            padding: '5px 14px',
                            fontSize: 16,
                            fontWeight: 700,
                            color: card.amount > 0 ? 'var(--green-light)' : 'var(--red)',
                        }}>
                            {card.amount > 0 ? '+' : ''}{card.amount < 0 ? `-$${Math.abs(card.amount).toLocaleString()}` : `$${card.amount.toLocaleString()}`}
                        </div>
                    )}
                </div>
                {/* Progress bar driven by CSS animation — completely outside React */}
                <div style={{height: 3, background: 'rgba(255,255,255,0.08)'}}>
                    <div style={{
                        height: '100%',
                        width: '100%',
                        background: accentColor,
                        transformOrigin: 'left',
                        animation: 'card-drain 5s linear forwards',
                    }}/>
                </div>
            </div>
        </div>
    );
}

/**
 * Confirmation modal for building a house or hotel on a property.
 * @param {{ prop: any, space: any, buildType: 'house'|'hotel', me: any, onConfirm: function, onClose: function }} props
 */
function BuildConfirmModal({prop, space, buildType, me, onConfirm, onClose}) {
    const cost = prop.houseCost || space?.houseCost || 0;
    const balanceAfter = me ? me.cash - cost : null;
    const canAfford = !me || me.cash >= cost;
    const isHotel = buildType === 'hotel';

    return (
        <div className="overlay">
            <div className="mbox" style={{maxWidth: 380}}>
                {space?.color && (
                    <div style={{
                        background: BCOLORS[space.color],
                        height: 34,
                        borderRadius: '8px 8px 0 0',
                        marginBottom: 14
                    }}/>
                )}
                <h3 style={{marginBottom: 4}}>{isHotel ? '🏨 Build Hotel' : '🏠 Build House'}</h3>
                <div style={{display: 'flex', alignItems: 'center', gap: 8, marginBottom: 14}}>
                    {space?.color && (
                        <div style={{
                            width: 8,
                            height: 18,
                            background: BCOLORS[space.color],
                            borderRadius: 2,
                            flexShrink: 0
                        }}/>
                    )}
                    <span style={{fontWeight: 600, fontSize: 15}}>{prop.name}</span>
                </div>
                {me && <ModalBalanceBar cash={me.cash}/>}
                <div style={{
                    background: 'var(--cream)', padding: 14, borderRadius: 10, marginBottom: 14,
                    display: 'flex', flexDirection: 'column', gap: 9,
                }}>
                    <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 14}}>
                        <span style={{color: '#aaa'}}>{isHotel ? 'Hotel' : 'House'} Cost</span>
                        <strong style={{color: 'var(--red)'}}>−${cost.toLocaleString()}</strong>
                    </div>
                    {balanceAfter !== null && (
                        <div style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            fontSize: 14,
                            borderTop: '1px solid var(--border)',
                            paddingTop: 9
                        }}>
                            <span style={{color: '#aaa'}}>Balance After</span>
                            <strong style={{color: canAfford ? 'var(--gold)' : 'var(--red)'}}>
                                ${balanceAfter.toLocaleString()}
                            </strong>
                        </div>
                    )}
                </div>
                {!canAfford && (
                    <div style={{
                        background: 'rgba(229,57,53,0.12)', color: 'var(--red)',
                        padding: 10, borderRadius: 8, fontSize: 13, marginBottom: 12,
                        textAlign: 'center', fontWeight: 600,
                    }}>
                        ⚠ Insufficient funds!
                    </div>
                )}
                <div style={{display: 'flex', gap: 9}}>
                    <button className="btn btn-green btn-full" disabled={!canAfford} onClick={() => {
                        onConfirm();
                        onClose();
                    }}>
                        ✓ Confirm
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

/** Resign confirmation modal. */
function ResignModal({onConfirm, onClose}) {
    return (
        <div className="overlay">
            <div className="mbox" style={{maxWidth: 360, textAlign: 'center'}}>
                <div style={{fontSize: 38, marginBottom: 10}}>🏳</div>
                <h3 style={{marginBottom: 8}}>Resign from game?</h3>
                <p style={{color: '#aaa', fontSize: 13, marginBottom: 22}}>
                    You will be eliminated and your properties returned to the bank. This cannot be undone.
                </p>
                <div style={{display: 'flex', gap: 9}}>
                    <button className="btn btn-red btn-full" onClick={() => {
                        onConfirm();
                        onClose();
                    }}>🏳 Yes, Resign
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
                                {/* Slot 1: jail button — holds its height always */}
                                <div style={{
                                    width: '100%',
                                    visibility: (isMyTurn && !paused && me?.isInJail) ? 'visible' : 'hidden',
                                    pointerEvents: (isMyTurn && !paused && me?.isInJail) ? 'auto' : 'none',
                                }}>
                                    <button className="btn btn-red btn-board btn-full"
                                            onClick={() => setJailModalOpen(true)}>
                                        ⛓ Handle Jail
                                    </button>
                                </div>

                                {/* Slot 2: main roll/buy/end row — always in flow */}
                                <div className="action-row" style={{
                                    visibility: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'visible' : 'hidden',
                                    pointerEvents: (isMyTurn && !paused && gameState?.status === 'InProgress') ? 'auto' : 'none',
                                }}>
                                    <button className="btn btn-gold btn-board" onClick={handleRoll}
                                            disabled={me?.hasRolledDice || rolling || me?.isInJail}>
                                        {rolling ? '…' : '🎲 Roll'}
                                    </button>
                                    <button className="btn btn-green btn-board" onClick={() => setBuyModalOpen(true)}
                                            disabled={!canBuy}>
                                        🏠 {canBuy ? `$${boardSpace?.purchasePrice || '?'}` : 'Buy'}
                                    </button>
                                    <button className="btn btn-ghost btn-board"
                                            onClick={() => hubCall('EndTurn', gameId)}
                                            disabled={!me?.hasRolledDice}>
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

                {/* PROPERTIES: sits between board and event log */}
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