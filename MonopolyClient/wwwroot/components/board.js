/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, React */

/**
 * Renders the Monopoly board with player tokens and property ownership.
 */
function Board({board, players}) {
    const [inspectSpace, setInspectSpace] = useState(null);

    const pm = {};
    (players || []).forEach((p, i) => {
        if (!pm[p.position]) {
            pm[p.position] = [];
        }
        pm[p.position].push({...p, ci: i});
    });

    const toks = (id) => (pm[id] || []).map((p, i) => (
        <div key={p.id} className="tok" style={{
            background: COLORS[p.ci % COLORS.length],
            top: 3 + i * 14,
            right: 3,
            width: 14,
            height: 14,
            fontSize: 7
        }} title={p.name}>
            {p.name[0]}
        </div>
    ));

    const bg = (space) => {
        const bp = board?.find(b => b.id === space.id);
        if (bp?.ownerId) {
            const idx = (players || []).findIndex(p => p.id === bp.ownerId);
            if (idx >= 0) {
                return {background: COLORS[idx % COLORS.length] + '22'};
            }
        }
        return {};
    };

    /** Maps a non-street space type/name to an appropriate emoji icon. */
    const getIcon = (s) => {
        if (s.type === 'Railroad') {
            return '🚂';
        }
        if (s.type === 'Utility') {
            if (s.name.includes('Water')) {
                return '💧';
            }
            if (s.name.includes('Electric')) {
                return '⚡';
            }
            return '🔌';
        }
        if (s.type === 'Tax') {
            if (s.name.includes('Luxury')) {
                return '💍';
            }
            return '💰';
        }
        if (s.type === 'Chance') {
            return '❓';
        }
        if (s.type === 'CommunityChest') {
            return '🏛';
        }
        if (s.type === 'Go') {
            return '🏁';
        }
        if (s.type === 'Jail') {
            return '⛓';
        }
        if (s.type === 'FreeParking') {
            return '🅿';
        }
        if (s.type === 'GoToJail') {
            return '🚔';
        }
        return '';
    };

    /** Returns a price string for any purchasable or taxable space. */
    const getCellPrice = (space, boardSpace) => {
        const price = boardSpace?.purchasePrice || space.price || space.amount || space.tax;
        if (!price) {
            return null;
        }
        return `$${price}`;
    };

    /** Renders an individual space cell on the board. */
    const Cell = ({space, layout, style = {}}) => {
        const boardSpace = board?.find(b => b.id === space.id);
        const isStreet = space.type === 'Street';
        const isPurchasable = ['Street', 'Railroad', 'Utility'].includes(space.type);
        const isTax = space.type === 'Tax';
        const showPrice = isPurchasable || isTax;
        const isVert = style.flexDirection === 'row' || style.flexDirection === 'row-reverse';
        const icon = getIcon(space);
        const priceLabel = getCellPrice(space, boardSpace);

        // Houses/hotels displayed on the color bar
        const buildingOverlay = (() => {
            if (!boardSpace) {
                return null;
            }
            if (boardSpace.hasHotel) {
                return <span style={{fontSize: 7, lineHeight: 1, userSelect: 'none'}}>🏨</span>;
            }
            if (boardSpace.houseCount > 0) {
                return (
                    <span style={{fontSize: 6, lineHeight: 1, letterSpacing: '-1px', userSelect: 'none'}}>
                        {'🏠'.repeat(boardSpace.houseCount)}
                    </span>
                );
            }
            return null;
        })();

        let bnameContent;

        if (isStreet) {
            if (layout === 'left') {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'row',
                        width: '100%',
                        height: '100%',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        padding: '2px'
                    }}>
                        <div style={{writingMode: 'vertical-rl', transform: 'rotate(180deg)', textAlign: 'center'}}>
                            {space.name}
                        </div>
                        {priceLabel && (
                            <div style={{
                                writingMode: 'vertical-rl',
                                transform: 'rotate(180deg)',
                                fontWeight: 800,
                                fontSize: 'clamp(5px, 0.6vw, 8px)'
                            }}>
                                {priceLabel}
                            </div>
                        )}
                    </div>
                );
            } else if (layout === 'right') {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'row',
                        width: '100%',
                        height: '100%',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        padding: '2px'
                    }}>
                        {priceLabel && (
                            <div style={{
                                writingMode: 'vertical-rl',
                                fontWeight: 800,
                                fontSize: 'clamp(5px, 0.6vw, 8px)'
                            }}>
                                {priceLabel}
                            </div>
                        )}
                        <div style={{writingMode: 'vertical-rl', textAlign: 'center'}}>
                            {space.name}
                        </div>
                    </div>
                );
            } else if (layout === 'bottom') {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'column',
                        width: '100%',
                        height: '100%',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        padding: '2px 0'
                    }}>
                        <div style={{
                            padding: '0 2px',
                            textAlign: 'center',
                            wordBreak: 'break-word',
                            overflowWrap: 'break-word'
                        }}>{space.name}</div>
                        {priceLabel && (
                            <div style={{fontSize: 'clamp(5px, 0.6vw, 8px)', fontWeight: 800}}>{priceLabel}</div>
                        )}
                    </div>
                );
            } else {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'column',
                        width: '100%',
                        height: '100%',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        padding: '2px 0'
                    }}>
                        <div style={{
                            padding: '0 2px',
                            textAlign: 'center',
                            wordBreak: 'break-word',
                            overflowWrap: 'break-word'
                        }}>{space.name}</div>
                        {priceLabel && (
                            <div style={{fontSize: 'clamp(5px, 0.6vw, 8px)', fontWeight: 800}}>{priceLabel}</div>
                        )}
                    </div>
                );
            }
        } else {
            // Non-street: icon + name + optional price
            if (layout === 'left') {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'row',
                        width: '100%',
                        height: '100%',
                        alignItems: 'center'
                    }}>
                        <div className="bname-strip" style={{writingMode: 'vertical-rl', transform: 'rotate(180deg)'}}>
                            {space.name}
                        </div>
                        <div style={{
                            flex: 1,
                            display: 'flex',
                            flexDirection: 'column',
                            alignItems: 'center',
                            justifyContent: 'center',
                            gap: 2
                        }}>
                            <div className="bicon" style={{transform: 'rotate(-90deg)'}}>{icon}</div>
                            {showPrice && priceLabel && (
                                <div style={{
                                    writingMode: 'vertical-rl',
                                    transform: 'rotate(180deg)',
                                    fontWeight: 800,
                                    fontSize: 'clamp(4px, 0.55vw, 7px)'
                                }}>
                                    {priceLabel}
                                </div>
                            )}
                        </div>
                    </div>
                );
            } else if (layout === 'right') {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'row',
                        width: '100%',
                        height: '100%',
                        alignItems: 'center'
                    }}>
                        <div style={{
                            flex: 1,
                            display: 'flex',
                            flexDirection: 'column',
                            alignItems: 'center',
                            justifyContent: 'center',
                            gap: 2
                        }}>
                            <div className="bicon" style={{transform: 'rotate(90deg)'}}>{icon}</div>
                            {showPrice && priceLabel && (
                                <div style={{
                                    writingMode: 'vertical-rl',
                                    fontWeight: 800,
                                    fontSize: 'clamp(4px, 0.55vw, 7px)'
                                }}>
                                    {priceLabel}
                                </div>
                            )}
                        </div>
                        <div className="bname-strip" style={{writingMode: 'vertical-rl'}}>
                            {space.name}
                        </div>
                    </div>
                );
            } else if (layout === 'bottom') {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'column',
                        width: '100%',
                        height: '100%',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        padding: '3px 2px'
                    }}>
                        <div className="bicon">{icon}</div>
                        <div style={{
                            fontSize: 'clamp(4px, 0.55vw, 6px)',
                            fontWeight: 700,
                            textAlign: 'center',
                            lineHeight: 1.1
                        }}>
                            {space.name}
                        </div>
                        {showPrice && priceLabel && (
                            <div style={{fontSize: 'clamp(4px, 0.55vw, 6px)', fontWeight: 800}}>{priceLabel}</div>
                        )}
                    </div>
                );
            } else {
                // top
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'column',
                        width: '100%',
                        height: '100%',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        padding: '3px 2px'
                    }}>
                        <div style={{
                            fontSize: 'clamp(4px, 0.55vw, 6px)',
                            fontWeight: 700,
                            textAlign: 'center',
                            lineHeight: 1.1
                        }}>
                            {space.name}
                        </div>
                        <div className="bicon">{icon}</div>
                        {showPrice && priceLabel && (
                            <div style={{fontSize: 'clamp(4px, 0.55vw, 6px)', fontWeight: 800}}>{priceLabel}</div>
                        )}
                    </div>
                );
            }
        }

        return (
            <div
                className="bspace"
                style={{...bg(space), ...style, cursor: 'pointer'}}
                onClick={() => setInspectSpace(space)}
            >
                {space.color && (
                    <div className="bcolor" style={{
                        background: BCOLORS[space.color] || '#ccc',
                        width: isVert ? '12px' : '100%',
                        height: isVert ? '100%' : '12px',
                        flexShrink: 0,
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        overflow: 'visible',
                        position: 'relative'
                    }}>
                        {buildingOverlay}
                    </div>
                )}
                <div className="bname">
                    {bnameContent}
                </div>
                {toks(space.id)}
            </div>
        );
    };

    /** Inspect modal shown when a space cell is clicked. */
    const InspectModal = () => {
        if (!inspectSpace) {
            return null;
        }

        const boardSpace = board?.find(b => b.id === inspectSpace.id);
        const owner = boardSpace?.ownerId ? players.find(p => p.id === boardSpace.ownerId) : null;

        return (
            <div className="overlay" onClick={() => setInspectSpace(null)}>
                <div className="mbox" onClick={e => e.stopPropagation()}>
                    <div style={{marginBottom: 16}}>
                        {inspectSpace.color && (
                            <div style={{
                                background: BCOLORS[inspectSpace.color],
                                height: 40,
                                borderRadius: '8px 8px 0 0',
                                marginBottom: 12
                            }}/>
                        )}
                        <h2 style={{fontSize: 24, marginBottom: 8, display: 'flex', alignItems: 'center', gap: 10}}>
                            {getIcon(inspectSpace) && (
                                <span style={{fontSize: 26}}>{getIcon(inspectSpace)}</span>
                            )}
                            {inspectSpace.name.replace('\n', ' ')}
                        </h2>
                        <div style={{fontSize: 13, color: '#999', marginBottom: 16}}>
                            {inspectSpace.type} • Position #{inspectSpace.id}
                        </div>
                    </div>

                    {boardSpace && (
                        <div style={{display: 'flex', flexDirection: 'column', gap: 10}}>
                            {boardSpace.purchasePrice && (
                                <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 14}}>
                                    <span style={{color: '#999'}}>Purchase Price</span>
                                    <strong style={{color: 'var(--green)'}}>${boardSpace.purchasePrice}</strong>
                                </div>
                            )}
                            {owner && (
                                <div style={{display: 'flex', justifyContent: 'space-between', fontSize: 14}}>
                                    <span style={{color: '#999'}}>Owner</span>
                                    <strong>{owner.name}</strong>
                                </div>
                            )}
                            {!owner && boardSpace.purchasePrice && (
                                <div style={{
                                    background: 'var(--cream)',
                                    padding: 10,
                                    borderRadius: 8,
                                    fontSize: 12,
                                    color: '#666',
                                    textAlign: 'center'
                                }}>
                                    Available for purchase
                                </div>
                            )}
                            {boardSpace.isMortgaged && (
                                <span className="badge bg-red" style={{alignSelf: 'flex-start'}}>Mortgaged</span>
                            )}
                            {boardSpace.houseCount > 0 && (
                                <div style={{fontSize: 14}}>
                                    🏠 {boardSpace.houseCount} house{boardSpace.houseCount !== 1 ? 's' : ''}
                                </div>
                            )}
                            {boardSpace.hasHotel && (<div style={{fontSize: 14}}>🏨 Hotel</div>)}
                        </div>
                    )}

                    <button
                        className="btn btn-ghost btn-full"
                        style={{marginTop: 20}}
                        onClick={() => setInspectSpace(null)}
                    >
                        Close
                    </button>
                </div>
            </div>
        );
    };

    return (
        <div className="bwrap">
            <div className="bgrid">
                {/* GO: bottom-right */}
                <div className="bcorner" style={{gridColumn: 11, gridRow: 11, background: '#e8f5e9'}}>
                    <span className="corner-icon">🏁</span>
                    <span className="corner-label">GO</span>
                    {toks(0)}
                </div>
                {/* Jail: bottom-left */}
                <div className="bcorner" style={{gridColumn: 1, gridRow: 11}}>
                    <span className="corner-icon">⛓</span>
                    <span className="corner-label">Jail</span>
                    {toks(10)}
                </div>
                {/* Free Parking: top-left */}
                <div className="bcorner" style={{gridColumn: 1, gridRow: 1}}>
                    <span className="corner-icon">🅿</span>
                    <span className="corner-label">Free</span>
                    {toks(20)}
                </div>
                {/* Go To Jail: top-right */}
                <div className="bcorner" style={{gridColumn: 11, gridRow: 1}}>
                    <span className="corner-icon">🚔</span>
                    <span className="corner-label">Jail!</span>
                    {toks(30)}
                </div>

                {/* Bottom row: spaces 1–9 */}
                {SPACES.slice(1, 10).map((s, i) => (
                    <div key={s.id} style={{gridColumn: 10 - i, gridRow: 11, display: 'flex', flexDirection: 'column'}}>
                        <Cell space={s} layout="bottom" style={{height: '100%', flexDirection: 'column'}}/>
                    </div>
                ))}

                {/* Left column: spaces 11–19 */}
                {SPACES.slice(11, 20).map((s, i) => (
                    <div key={s.id} style={{gridColumn: 1, gridRow: 10 - i, display: 'flex'}}>
                        <Cell space={s} layout="left" style={{height: '100%', width: '100%', flexDirection: 'row'}}/>
                    </div>
                ))}

                {/* Top row: spaces 21–29 */}
                {SPACES.slice(21, 30).map((s, i) => (
                    <div key={s.id} style={{gridColumn: 2 + i, gridRow: 1, display: 'flex', flexDirection: 'column'}}>
                        <Cell space={s} layout="top" style={{height: '100%', flexDirection: 'column'}}/>
                    </div>
                ))}

                {/* Right column: spaces 31–39 */}
                {SPACES.slice(31, 40).map((s, i) => (
                    <div key={s.id} style={{gridColumn: 11, gridRow: 2 + i, display: 'flex'}}>
                        <Cell space={s} layout="right"
                              style={{height: '100%', width: '100%', flexDirection: 'row-reverse'}}/>
                    </div>
                ))}

                {/* Center */}
                <div className="bcenter">
                    <div style={{
                        fontFamily: 'Playfair Display,serif',
                        fontSize: 'clamp(11px,2vw,19px)',
                        fontWeight: 900,
                        textAlign: 'center'
                    }}>
                        MONOPOLY
                    </div>
                    <div style={{fontSize: 8, color: '#bbb', marginTop: 2}}>Online Edition</div>
                    <div className="board-card-stacks">
                        <div className="board-card-stack">
                            <div className="card-deck">
                                <div className="card-deck-layer"
                                     style={{background: '#fff9c4', borderColor: '#b8860b44'}}/>
                                <div className="card-deck-layer"
                                     style={{background: '#fff9c4', borderColor: '#b8860b66'}}/>
                                <div className="card-deck-layer"
                                     style={{background: '#fff9c4', borderColor: '#b8860b99', color: '#b8860b'}}>❓
                                </div>
                            </div>
                            <div className="board-card-stack-label">Chance</div>
                        </div>
                        <div className="board-card-stack">
                            <div className="card-deck">
                                <div className="card-deck-layer"
                                     style={{background: '#c8e6c9', borderColor: '#2d6a4f44'}}/>
                                <div className="card-deck-layer"
                                     style={{background: '#c8e6c9', borderColor: '#2d6a4f66'}}/>
                                <div className="card-deck-layer"
                                     style={{background: '#c8e6c9', borderColor: '#2d6a4f99', color: '#2d6a4f'}}>🏛
                                </div>
                            </div>
                            <div className="board-card-stack-label">Comm. Chest</div>
                        </div>
                    </div>
                </div>
            </div>

            <InspectModal/>
        </div>
    );
}