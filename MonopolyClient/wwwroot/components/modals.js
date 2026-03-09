/* globals useState, useContext, Ctx, COLORS, BCOLORS, SPACES, gameHub, React */

// components/modals.js — all modal dialogs and shared property-list helpers.
// Depends on constants.js (SPACES, BCOLORS, COLORS) and signalr.js (gameHub).

// ─── Shared Helpers ───────────────────────────────────────────────────────────

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

/** Color swatch + label for a color group header in trade/property panels. */
const ColorGroupHeader = ({color}) => (
    <div className="color-group-header">
        {color !== '__other' && (
            <div className="color-group-swatch" style={{background: BCOLORS[color] || '#ccc'}}/>
        )}
        <span className="color-group-label">
            {color === '__other' ? 'Other' : color}
        </span>
    </div>
);

// ─── ModalBalanceBar ──────────────────────────────────────────────────────────

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

// ─── JailModal ────────────────────────────────────────────────────────────────

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

// ─── IncomingTradeModal ───────────────────────────────────────────────────────

/**
 * Modal shown when another player sends a trade proposal.
 * @param {{ offer: any, me: any, gameId: string, board: any[], onDone: function }} props
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
                                ? <div className="prop-swatch" style={{height: 14, background: BCOLORS[space.color]}}/>
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
                <div className="modal-btn-row">
                    <button className="btn btn-green btn-full" onClick={handleAccept}>✓ Accept</button>
                    <button className="btn btn-red btn-full" onClick={handleReject}>✕ Decline</button>
                </div>
            </div>
        </div>
    );
}

// ─── ProposeTradeModal ────────────────────────────────────────────────────────

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
        console.log('[TRADE] Sending ProposeTrade', {gameId, toPlayerId: target.id, toPlayerName: target.name, payload});
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
                                <div className="prop-swatch" style={{height: 14, background: BCOLORS[prop.space.color]}}/>
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
                            display: 'flex', alignItems: 'center',
                            justifyContent: 'space-between', marginBottom: 9
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
                <div className="modal-btn-row">
                    <button className="btn btn-green btn-full" onClick={handleSend}>📤 Send Proposal</button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

// ─── BuyPropertyModal ─────────────────────────────────────────────────────────

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
                        <div className="modal-color-bar" style={{background: BCOLORS[space.color], height: 50}}/>
                    )}
                    <h2 style={{fontSize: 22, marginBottom: 6}}>Purchase Property?</h2>
                    <div style={{fontSize: 18, fontWeight: 700, color: 'var(--ink)', marginBottom: 4}}>
                        {space.name.replace('\n', ' ')}
                    </div>
                    <div style={{fontSize: 13, color: '#999'}}>
                        {space.type} • Position #{space.id}
                    </div>
                </div>

                <div className="modal-detail-panel">
                    <div className="modal-detail-row" style={{marginBottom: 12, fontSize: 15}}>
                        <span style={{color: '#666'}}>Purchase Price</span>
                        <strong style={{fontSize: 18, color: 'var(--gold)'}}>${price.toLocaleString()}</strong>
                    </div>
                    <div className="modal-detail-row" style={{marginBottom: 12}}>
                        <span style={{color: '#666'}}>Your Balance</span>
                        <strong style={{color: 'var(--green)'}}>${me.cash.toLocaleString()}</strong>
                    </div>
                    <div className="modal-detail-row-total">
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
                                            <div className="prop-swatch"
                                                 style={{height: 20, background: BCOLORS[propSpace.color]}}/>
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
                    <div className="modal-error-banner" style={{marginBottom: 16}}>
                        ⚠ Insufficient funds!
                    </div>
                )}

                <div className="modal-btn-row">
                    <button className="btn btn-green btn-full btn-lg" onClick={handleConfirm} disabled={!canAfford}>
                        ✓ Confirm Purchase
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

// ─── MortgageConfirmModal ─────────────────────────────────────────────────────

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
                    <div className="modal-color-bar" style={{background: BCOLORS[space.color]}}/>
                )}
                <h3 style={{marginBottom: 4}}>{isMortgaged ? 'Unmortgage' : 'Mortgage'} Property?</h3>
                <div style={{display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12}}>
                    {space?.color && (
                        <div className="prop-swatch" style={{height: 18, background: BCOLORS[space.color]}}/>
                    )}
                    <span style={{fontWeight: 600, fontSize: 15}}>{prop.name}</span>
                    <span style={{color: '#aaa', fontSize: 12, marginLeft: 'auto'}}>
                        ${purchasePrice.toLocaleString()}
                    </span>
                </div>
                {me && <ModalBalanceBar cash={me.cash}/>}
                <div className="modal-detail-panel">
                    {!isMortgaged ? (
                        <>
                            <div className="modal-detail-row">
                                <span style={{color: '#aaa'}}>Purchase Price</span>
                                <strong>${purchasePrice.toLocaleString()}</strong>
                            </div>
                            <div className="modal-detail-row-total">
                                <span style={{color: '#aaa'}}>You will receive</span>
                                <strong style={{color: 'var(--green)'}}>+${mortgageValue.toLocaleString()}</strong>
                            </div>
                            {balanceAfter !== null && (
                                <div className="modal-detail-row" style={{fontSize: 13}}>
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
                            <div className="modal-detail-row">
                                <span style={{color: '#aaa'}}>Mortgage Value</span>
                                <strong>${mortgageValue.toLocaleString()}</strong>
                            </div>
                            <div className="modal-detail-row-total">
                                <span style={{color: '#aaa'}}>Cost to Unmortgage</span>
                                <strong style={{color: 'var(--red)'}}>−${unmortgageCost.toLocaleString()}</strong>
                            </div>
                            {balanceAfter !== null && (
                                <div className="modal-detail-row" style={{fontSize: 13}}>
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
                    <div className="modal-error-banner">⚠ Insufficient funds to unmortgage!</div>
                )}
                <div className="modal-btn-row">
                    <button
                        className={`btn btn-full ${isMortgaged ? 'btn-green' : 'btn-red'}`}
                        disabled={!canAfford}
                        onClick={() => {
                            onConfirm();
                            onClose();
                        }}
                    >
                        {isMortgaged
                            ? `✓ Unmortgage for $${unmortgageCost.toLocaleString()}`
                            : `✓ Mortgage for $${mortgageValue.toLocaleString()}`}
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

// ─── LiquidateBuildingsModal ──────────────────────────────────────────────────

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
        const houseCost = prop.houseCost || space?.houseCost || 0;
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
                                <div className="prop-swatch" style={{height: 20, background: BCOLORS[space.color] || '#ccc'}}/>
                            )}
                            <div style={{flex: 1, minWidth: 0}}>
                                <div style={{fontWeight: 600, fontSize: 12}}>{prop.name}</div>
                                <div style={{fontSize: 11, color: '#aaa'}}>
                                    {info.label}
                                    {info.perUnit > 0 && ` · $${info.perUnit} each`}
                                    {space?.houseCost && (
                                        <span style={{color: '#888'}}> (build cost: ${space.houseCost})</span>
                                    )}
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
                        <div className="modal-detail-row">
                            <span style={{color: '#aaa', fontSize: 13}}>Total payout</span>
                            <strong style={{fontSize: 18, color: 'var(--green)'}}>+${totalPayout.toLocaleString()}</strong>
                        </div>
                        {balanceAfter !== null && totalPayout > 0 && (
                            <div className="modal-detail-row" style={{fontSize: 12}}>
                                <span style={{color: '#aaa'}}>Balance after</span>
                                <strong style={{color: 'var(--gold)', fontSize: 14}}>
                                    ${balanceAfter.toLocaleString()}
                                </strong>
                            </div>
                        )}
                    </div>
                )}

                <div className="modal-btn-row" style={{marginTop: 6}}>
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

// ─── CardDrawnModal ───────────────────────────────────────────────────────────

// Card types that give the player something good (green / plus).
// String names match CardType enum; numeric indices guard against C# default int serialization.
// CardType enum order: MoveToGo=0, MoveToJail=1, MoveToJustVisiting=2, MoveForward=3,
//   MoveBackward=4, MoveToSpecificLocation=5, PayBank=6, CollectFromBank=7,
//   PayEachPlayer=8, CollectFromEachPlayer=9, PayForHouseRepairs=10, GetOutOfJailFree=11, Advance=12
const POSITIVE_CARD_TYPES = new Set([
    'MoveToGo', 0,
    'MoveToJustVisiting', 2,
    'MoveForward', 3,
    'CollectFromBank', 7,
    'CollectFromEachPlayer', 9,
    'GetOutOfJailFree', 11,
    'Advance', 12,
]);

// Card types that cost the player something (red / minus).
const NEGATIVE_CARD_TYPES = new Set([
    'MoveToJail', 1,
    'MoveBackward', 4,
    'PayBank', 6,
    'PayEachPlayer', 8,
    'PayForHouseRepairs', 10,
]);

/** Returns 'positive', 'negative', or 'neutral' for a CardType enum value (string or integer). */
const getCardSentiment = (type) => {
    if (POSITIVE_CARD_TYPES.has(type)) { return 'positive'; }
    if (NEGATIVE_CARD_TYPES.has(type)) { return 'negative'; }
    // MoveToSpecificLocation (5) and unknown types fall through; amount sign is the tiebreaker.
    return 'neutral';
};

/**
 * Purely presentational card reveal modal. Timer and dismiss logic live in GamePage.
 * Progress bar is driven by a CSS animation — zero JS ticking, zero state, zero re-renders.
 * @param {{ card: {type: string, deck?: string, text: string, amount?: number}, onDismiss: function }} props
 */
function CardDrawnModal({card, onDismiss}) {
    const isChance = card.deck === 'Chance' || card.type === 'Chance';
    const accentColor = isChance ? '#b8860b' : '#2d6a4f';
    const bgColor = isChance ? '#fff9c4' : '#c8e6c9';
    const icon = isChance ? '❓' : '🏛';

    const sentiment = getCardSentiment(card.type);
    // Positive sentiment or positive amount → green; negative sentiment or negative amount → red.
    const isPositive = sentiment === 'positive' || (sentiment === 'neutral' && card.amount > 0);
    const amountColor = isPositive ? 'var(--green-light)' : 'var(--red)';
    const amountBg = isPositive ? 'rgba(26,158,120,0.15)' : 'rgba(229,57,53,0.15)';
    const amountBorder = isPositive ? 'rgba(26,158,120,0.3)' : 'rgba(229,57,53,0.3)';
    const amountPrefix = isPositive ? '+' : '−';
    const showAmount = card.amount != null && card.amount !== 0;

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
                        marginBottom: showAmount ? 14 : 0,
                    }}>{card.text}</p>
                    {showAmount && (
                        <div style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: 6,
                            background: amountBg,
                            border: `1px solid ${amountBorder}`,
                            borderRadius: 8,
                            padding: '5px 14px',
                            fontSize: 16,
                            fontWeight: 700,
                            color: amountColor,
                        }}>
                            {amountPrefix}${Math.abs(card.amount).toLocaleString()}
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

// ─── BuildConfirmModal ────────────────────────────────────────────────────────

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
                    <div className="modal-color-bar" style={{background: BCOLORS[space.color], height: 34}}/>
                )}
                <h3 style={{marginBottom: 4}}>{isHotel ? '🏨 Build Hotel' : '🏠 Build House'}</h3>
                <div style={{display: 'flex', alignItems: 'center', gap: 8, marginBottom: 14}}>
                    {space?.color && (
                        <div className="prop-swatch" style={{height: 18, background: BCOLORS[space.color]}}/>
                    )}
                    <span style={{fontWeight: 600, fontSize: 15}}>{prop.name}</span>
                </div>
                {me && <ModalBalanceBar cash={me.cash}/>}
                <div className="modal-detail-panel">
                    <div className="modal-detail-row">
                        <span style={{color: '#aaa'}}>{isHotel ? 'Hotel' : 'House'} Cost</span>
                        <strong style={{color: 'var(--red)'}}>−${cost.toLocaleString()}</strong>
                    </div>
                    {balanceAfter !== null && (
                        <div className="modal-detail-row-total">
                            <span style={{color: '#aaa'}}>Balance After</span>
                            <strong style={{color: canAfford ? 'var(--gold)' : 'var(--red)'}}>
                                ${balanceAfter.toLocaleString()}
                            </strong>
                        </div>
                    )}
                </div>
                {!canAfford && (
                    <div className="modal-error-banner">⚠ Insufficient funds!</div>
                )}
                <div className="modal-btn-row">
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

// ─── ResignModal ──────────────────────────────────────────────────────────────

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
                <div className="modal-btn-row">
                    <button className="btn btn-red btn-full" onClick={() => {
                        onConfirm();
                        onClose();
                    }}>
                        🏳 Yes, Resign
                    </button>
                    <button className="btn btn-ghost btn-full" onClick={onClose}>Cancel</button>
                </div>
            </div>
        </div>
    );
}