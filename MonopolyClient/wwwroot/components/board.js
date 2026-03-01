/* globals useState, useEffect, useRef, useCallback, COLORS, BCOLORS, SPACES, React, DiceTray */

/**
 * Renders the Monopoly board with player tokens, property ownership, inline dice, and action panel.
 * @param {{ board: any[], players: any[], animatedPositions: Object, dice: number[], rolling: boolean, actionPanel: any }} props
 */
function Board({board, players, animatedPositions = {}, dice = [], rolling = false, actionPanel = null}) {
    const [inspectSpace, setInspectSpace] = useState(null);
    const wrapRef = useRef(null);
    const cellRefs = useRef({});
    const [cellPos, setCellPos] = useState({});
    const [boardWidth, setBoardWidth] = useState(520);

    /** Re-measures all cell bounding rects and captures board width for inline scaling. */
    const measureCells = useCallback(() => {
        if (!wrapRef.current) {
            return;
        }
        const wrap = wrapRef.current.getBoundingClientRect();
        setBoardWidth(wrap.width || 520);
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

    // Scale factor relative to 520px baseline
    const bs = boardWidth / 520;

    // Token sizing derived from board scale
    const tokSize = Math.max(10, Math.round(14 * bs));
    const tokFont = Math.max(6, Math.round(7 * bs));
    const tokStack = Math.round(16 * bs);

    const activePlayers = players || [];

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

    /** Returns the price label or non-breaking space so the price slot is always filled. */
    const getPriceDisplay = (space, boardSpace) => {
        const price = boardSpace?.purchasePrice || space.price || space.amount || space.tax;
        return price ? `$${price}` : '\u00A0';
    };

    // Inline font sizes scaled to board width
    const priceSz = `clamp(5px, ${(1.1 * bs).toFixed(2)}cqw, ${Math.round(8 * bs)}px)`;
    const stripPriceSz = `clamp(4px, ${(0.9 * bs).toFixed(2)}cqw, ${Math.round(7 * bs)}px)`;
    const buildingFontLg = Math.max(5, Math.round(7 * bs));
    const buildingFontSm = Math.max(4, Math.round(6 * bs));
    const colorBarSz = `${Math.round(12 * bs)}px`;

    /** Renders a single board space cell with scaled text, icons, and price placeholder. */
    const Cell = ({space, layout, style = {}}) => {
        const boardSpace = board?.find(b => b.id === space.id);
        const isStreet = space.type === 'Street';
        const isPurchasable = ['Street', 'Railroad', 'Utility'].includes(space.type);
        const isTax = space.type === 'Tax';
        const showPrice = isPurchasable || isTax;
        const isVert = style.flexDirection === 'row' || style.flexDirection === 'row-reverse';
        const icon = getIcon(space);
        const priceDisplay = getPriceDisplay(space, boardSpace);

        const buildingOverlay = (() => {
            if (!boardSpace) {
                return null;
            }
            if (boardSpace.hasHotel) {
                return <span style={{fontSize: buildingFontLg, lineHeight: 1, userSelect: 'none'}}>🏨</span>;
            }
            if (boardSpace.houseCount > 0) {
                return (
                    <span style={{fontSize: buildingFontSm, lineHeight: 1, letterSpacing: '-1px', userSelect: 'none'}}>
                        {'🏠'.repeat(boardSpace.houseCount)}
                    </span>
                );
            }
            return null;
        })();

        const priceStyle = {fontSize: priceSz, fontWeight: 800, opacity: showPrice ? 1 : 0};
        const stripPriceStyleV = {
            writingMode: 'vertical-rl',
            fontWeight: 800,
            fontSize: stripPriceSz,
            padding: '0 2px',
            opacity: showPrice ? 1 : 0
        };

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
                        <div style={{...stripPriceStyleV, transform: 'rotate(180deg)', opacity: 1}}>{priceDisplay}</div>
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
                        <div style={{...stripPriceStyleV, opacity: 1}}>{priceDisplay}</div>
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
                        <div style={{...priceStyle, opacity: 1}}>{priceDisplay}</div>
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
                        <div style={{...priceStyle, opacity: 1}}>{priceDisplay}</div>
                    </div>
                );
            }
        } else {
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
                        <div style={{...stripPriceStyleV, transform: 'rotate(180deg)'}}>{priceDisplay}</div>
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
                        <div style={stripPriceStyleV}>{priceDisplay}</div>
                        <div style={{flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center'}}>
                            <div className="bicon" style={{transform: 'rotate(90deg)'}}>{icon}</div>
                        </div>
                        <div className="bname-strip" style={{writingMode: 'vertical-rl'}}>{space.name}</div>
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
                        <div style={{
                            fontSize: stripPriceSz,
                            fontWeight: 700,
                            textAlign: 'center',
                            lineHeight: 1.1
                        }}>{space.name}</div>
                        <div className="bicon">{icon}</div>
                        <div style={priceStyle}>{priceDisplay}</div>
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
                        padding: '3px 2px'
                    }}>
                        <div style={{
                            fontSize: stripPriceSz,
                            fontWeight: 700,
                            textAlign: 'center',
                            lineHeight: 1.1
                        }}>{space.name}</div>
                        <div className="bicon">{icon}</div>
                        <div style={priceStyle}>{priceDisplay}</div>
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
                        width: isVert ? colorBarSz : '100%',
                        height: isVert ? '100%' : colorBarSz,
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
                    {/* Title */}
                    <div style={{display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 0}}>
                        <div style={{
                            fontFamily: 'Playfair Display,serif',
                            fontSize: 'clamp(14px, 3cqw, 28px)',
                            fontWeight: 900,
                            textAlign: 'center',
                            letterSpacing: '0.05em'
                        }}>
                            MONOPOLY
                        </div>
                        <div style={{
                            fontSize: 'clamp(7px, 1.3cqw, 12px)',
                            color: '#aaa',
                            letterSpacing: '0.15em',
                            textTransform: 'uppercase'
                        }}>Online Edition
                        </div>
                    </div>

                    {/* Dice — sum right, doubles below */}
                    {(dice[0] && dice[1])
                        ? <DiceTray dice={dice} rolling={rolling}/>
                        : (
                            <div className="darea" style={{opacity: 0.3}}>
                                <div style={{display: 'flex', alignItems: 'center', gap: 8}}>
                                    <div className="die">
                                        {[false, false, false, false, true, false, false, false, false].map((on, i) => (
                                            <div key={i} className={`pip${on ? '' : ' off'}`}/>
                                        ))}
                                    </div>
                                    <div className="die">
                                        {[false, false, false, false, true, false, false, false, false].map((on, i) => (
                                            <div key={i} className={`pip${on ? '' : ' off'}`}/>
                                        ))}
                                    </div>
                                    <div style={{
                                        fontSize: 'clamp(10px, 2cqw, 16px)',
                                        color: '#888',
                                        fontWeight: 700,
                                        minWidth: '2em'
                                    }}>&nbsp;&nbsp;&nbsp;</div>
                                </div>
                                <div className="board-dice-doubles-slot">&nbsp;</div>
                            </div>
                        )
                    }

                    {/* Action panel */}
                    {actionPanel}

                    {/* Divider */}
                    <div style={{
                        width: '65%',
                        height: '1px',
                        background: 'rgba(0,0,0,0.08)',
                        margin: 'clamp(3px, 0.6cqw, 6px) 0'
                    }}/>

                    {/* Card stacks — 25% bigger than previous */}
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

            {/* Token overlay — persistent DOM elements with CSS-transitioned position */}
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
                            top: cell.y + 3 + stackIdx * tokStack,
                            left: cell.x + cell.w - tokSize - 2,
                            background: COLORS[i % COLORS.length],
                            color: '#fff',
                            width: tokSize,
                            height: tokSize,
                            fontSize: tokFont,
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