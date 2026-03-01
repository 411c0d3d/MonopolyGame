/* globals useState, useEffect, useRef, useCallback, COLORS, BCOLORS, SPACES, React */

/**
 * Renders the Monopoly board with player tokens and property ownership.
 * Tokens are rendered in an absolute overlay so the same DOM element persists
 * across position changes — enabling CSS transition-based hop animation.
 * @param {{ board: any[], players: any[], animatedPositions: Object }} props
 */
function Board({board, players, animatedPositions = {}}) {
    const [inspectSpace, setInspectSpace] = useState(null);
    const wrapRef = useRef(null);
    const cellRefs = useRef({});
    const [cellPos, setCellPos] = useState({});

    /** Re-measures all registered cell bounding rects relative to the board wrapper. */
    const measureCells = useCallback(() => {
        if (!wrapRef.current) {
            return;
        }
        const wrap = wrapRef.current.getBoundingClientRect();
        const next = {};
        Object.entries(cellRefs.current).forEach(([id, el]) => {
            if (!el) {
                return;
            }
            const r = el.getBoundingClientRect();
            next[Number(id)] = {x: r.left - wrap.left, y: r.top - wrap.top, w: r.width, h: r.height};
        });
        setCellPos(next);
    }, []);

    useEffect(() => {
        measureCells();
        const ro = new ResizeObserver(measureCells);
        if (wrapRef.current) {
            ro.observe(wrapRef.current);
        }
        return () => ro.disconnect();
    }, [measureCells]);

    const activePlayers = players || [];

    // Group players by current display position for token stacking offset
    const playersByPos = {};
    activePlayers.forEach((p, i) => {
        const pos = animatedPositions[p.id] ?? p.position;
        if (!playersByPos[pos]) {
            playersByPos[pos] = [];
        }
        playersByPos[pos].push({...p, colorIdx: i});
    });

    const bg = (space) => {
        const bp = board?.find(b => b.id === space.id);
        if (bp?.ownerId) {
            const idx = activePlayers.findIndex(p => p.id === bp.ownerId);
            if (idx >= 0) {
                return {background: COLORS[idx % COLORS.length] + '22'};
            }
        }
        return {};
    };

    /** Maps a space to its display emoji icon. */
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
            return s.name.includes('Luxury') ? '💍' : '💰';
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

    /** Returns the price label for any purchasable or taxable space. */
    const getCellPrice = (space, boardSpace) => {
        const price = boardSpace?.purchasePrice || space.price || space.amount || space.tax;
        return price ? `$${price}` : null;
    };

    /** Renders a single board space cell with responsive text and icon positioning. */
    const Cell = ({space, layout, style = {}}) => {
        const boardSpace = board?.find(b => b.id === space.id);
        const isStreet = space.type === 'Street';
        const isPurchasable = ['Street', 'Railroad', 'Utility'].includes(space.type);
        const isTax = space.type === 'Tax';
        const showPrice = isPurchasable || isTax;
        const isVert = style.flexDirection === 'row' || style.flexDirection === 'row-reverse';
        const icon = getIcon(space);
        const priceLabel = getCellPrice(space, boardSpace);

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
                        <div style={{
                            writingMode: 'vertical-rl',
                            transform: 'rotate(180deg)',
                            textAlign: 'center'
                        }}>{space.name}</div>
                        {priceLabel && <div style={{
                            writingMode: 'vertical-rl',
                            transform: 'rotate(180deg)',
                            fontWeight: 800,
                            fontSize: 'clamp(5px, 0.6vw, 8px)'
                        }}>{priceLabel}</div>}
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
                        {priceLabel && <div style={{
                            writingMode: 'vertical-rl',
                            fontWeight: 800,
                            fontSize: 'clamp(5px, 0.6vw, 8px)'
                        }}>{priceLabel}</div>}
                        <div style={{writingMode: 'vertical-rl', textAlign: 'center'}}>{space.name}</div>
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
                        {priceLabel &&
                            <div style={{fontSize: 'clamp(5px, 0.6vw, 8px)', fontWeight: 800}}>{priceLabel}</div>}
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
                        {priceLabel &&
                            <div style={{fontSize: 'clamp(5px, 0.6vw, 8px)', fontWeight: 800}}>{priceLabel}</div>}
                    </div>
                );
            }
        } else {
            // Non-street: name at top → icon centred → price at bottom (all layouts)
            if (layout === 'left') {
                bnameContent = (
                    <div style={{
                        display: 'flex',
                        flexDirection: 'row',
                        width: '100%',
                        height: '100%',
                        alignItems: 'center'
                    }}>
                        <div className="bname-strip"
                             style={{writingMode: 'vertical-rl', transform: 'rotate(180deg)'}}>{space.name}</div>
                        <div style={{flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
                            <div className="bicon" style={{transform: 'rotate(-90deg)'}}>{icon}</div>
                        </div>
                        {showPrice && priceLabel && (
                            <div style={{
                                writingMode: 'vertical-rl',
                                transform: 'rotate(180deg)',
                                fontWeight: 800,
                                fontSize: 'clamp(4px, 0.55vw, 7px)',
                                padding: '0 2px'
                            }}>{priceLabel}</div>
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
                        alignItems: 'center'
                    }}>
                        {showPrice && priceLabel && (
                            <div style={{
                                writingMode: 'vertical-rl',
                                fontWeight: 800,
                                fontSize: 'clamp(4px, 0.55vw, 7px)',
                                padding: '0 2px'
                            }}>{priceLabel}</div>
                        )}
                        <div style={{flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
                            <div className="bicon" style={{transform: 'rotate(90deg)'}}>{icon}</div>
                        </div>
                        <div className="bname-strip" style={{writingMode: 'vertical-rl'}}>{space.name}</div>
                    </div>
                );
            } else if (layout === 'bottom') {
                // Fixed: was icon→name→price, corrected to name→icon→price
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
                        }}>{space.name}</div>
                        <div className="bicon">{icon}</div>
                        {showPrice && priceLabel &&
                            <div style={{fontSize: 'clamp(4px, 0.55vw, 6px)', fontWeight: 800}}>{priceLabel}</div>}
                    </div>
                );
            } else {
                // top: name→icon→price (already was correct)
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
                        }}>{space.name}</div>
                        <div className="bicon">{icon}</div>
                        {showPrice && priceLabel &&
                            <div style={{fontSize: 'clamp(4px, 0.55vw, 6px)', fontWeight: 800}}>{priceLabel}</div>}
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
                        position: 'relative',
                    }}>
                        {buildingOverlay}
                    </div>
                )}
                <div className="bname">{bnameContent}</div>
            </div>
        );
    };

    /** Inspect modal shown when a space cell is clicked. */
    const InspectModal = () => {
        if (!inspectSpace) {
            return null;
        }
        const boardSpace = board?.find(b => b.id === inspectSpace.id);
        const owner = boardSpace?.ownerId ? activePlayers.find(p => p.id === boardSpace.ownerId) : null;

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
                            {getIcon(inspectSpace) && <span style={{fontSize: 26}}>{getIcon(inspectSpace)}</span>}
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
                            {boardSpace.isMortgaged &&
                                <span className="badge bg-red" style={{alignSelf: 'flex-start'}}>Mortgaged</span>}
                            {boardSpace.houseCount > 0 && <div
                                style={{fontSize: 14}}>🏠 {boardSpace.houseCount} house{boardSpace.houseCount !== 1 ? 's' : ''}</div>}
                            {boardSpace.hasHotel && <div style={{fontSize: 14}}>🏨 Hotel</div>}
                        </div>
                    )}
                    <button className="btn btn-ghost btn-full" style={{marginTop: 20}}
                            onClick={() => setInspectSpace(null)}>Close
                    </button>
                </div>
            </div>
        );
    };

    return (
        <div className="bwrap" ref={wrapRef} style={{position: 'relative'}}>
            <div className="bgrid">
                {/* Corners */}
                <div ref={el => {
                    if (el) {
                        cellRefs.current[0] = el;
                    }
                }} className="bcorner" style={{gridColumn: 11, gridRow: 11, background: '#e8f5e9'}}>
                    <span className="corner-icon">🏁</span><span className="corner-label">GO</span>
                </div>
                <div ref={el => {
                    if (el) {
                        cellRefs.current[10] = el;
                    }
                }} className="bcorner" style={{gridColumn: 1, gridRow: 11}}>
                    <span className="corner-icon">⛓</span><span className="corner-label">Jail</span>
                </div>
                <div ref={el => {
                    if (el) {
                        cellRefs.current[20] = el;
                    }
                }} className="bcorner" style={{gridColumn: 1, gridRow: 1}}>
                    <span className="corner-icon">🅿</span><span className="corner-label">Free</span>
                </div>
                <div ref={el => {
                    if (el) {
                        cellRefs.current[30] = el;
                    }
                }} className="bcorner" style={{gridColumn: 11, gridRow: 1}}>
                    <span className="corner-icon">🚔</span><span className="corner-label">Jail!</span>
                </div>

                {/* Bottom row: spaces 1–9 */}
                {SPACES.slice(1, 10).map((s, i) => (
                    <div ref={el => {
                        if (el) {
                            cellRefs.current[s.id] = el;
                        }
                    }} key={s.id}
                         style={{gridColumn: 10 - i, gridRow: 11, display: 'flex', flexDirection: 'column'}}>
                        <Cell space={s} layout="bottom" style={{height: '100%', flexDirection: 'column'}}/>
                    </div>
                ))}

                {/* Left column: spaces 11–19 */}
                {SPACES.slice(11, 20).map((s, i) => (
                    <div ref={el => {
                        if (el) {
                            cellRefs.current[s.id] = el;
                        }
                    }} key={s.id}
                         style={{gridColumn: 1, gridRow: 10 - i, display: 'flex'}}>
                        <Cell space={s} layout="left" style={{height: '100%', width: '100%', flexDirection: 'row'}}/>
                    </div>
                ))}

                {/* Top row: spaces 21–29 */}
                {SPACES.slice(21, 30).map((s, i) => (
                    <div ref={el => {
                        if (el) {
                            cellRefs.current[s.id] = el;
                        }
                    }} key={s.id}
                         style={{gridColumn: 2 + i, gridRow: 1, display: 'flex', flexDirection: 'column'}}>
                        <Cell space={s} layout="top" style={{height: '100%', flexDirection: 'column'}}/>
                    </div>
                ))}

                {/* Right column: spaces 31–39 */}
                {SPACES.slice(31, 40).map((s, i) => (
                    <div ref={el => {
                        if (el) {
                            cellRefs.current[s.id] = el;
                        }
                    }} key={s.id}
                         style={{gridColumn: 11, gridRow: 2 + i, display: 'flex'}}>
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
                    }}>MONOPOLY
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

            {/*
              Token overlay — each player has one persistent DOM element whose top/left
              transitions smoothly as animatedPositions steps through cells one by one.
            */}
            {activePlayers.map((p, i) => {
                const displayPos = animatedPositions[p.id] ?? p.position;
                const cell = cellPos[displayPos];
                if (!cell) {
                    return null;
                }
                const stackPeers = playersByPos[displayPos] || [];
                const stackIdx = stackPeers.findIndex(pp => pp.id === p.id);
                return (
                    <div
                        key={p.id}
                        className="tok player-token"
                        title={p.name}
                        style={{
                            position: 'absolute',
                            top: cell.y + 3 + stackIdx * 14,
                            left: cell.x + cell.w - 17,
                            background: COLORS[i % COLORS.length],
                            color: '#fff',
                            width: 14,
                            height: 14,
                            fontSize: 7,
                            zIndex: 10,
                            pointerEvents: 'none',
                        }}
                    >
                        {p.name[0]}
                    </div>
                );
            })}

            <InspectModal/>
        </div>
    );
}