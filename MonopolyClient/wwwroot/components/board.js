/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, React */

/**
 * Renders the Monopoly board with player tokens and property ownership.
 */
function Board({ board, players }) {
    const [inspectSpace, setInspectSpace] = useState(null);

    const pm = {};
    (players || []).forEach((p, i) => {
        if (!pm[p.position]) {
            pm[p.position] = [];
        }
        pm[p.position].push({ ...p, ci: i });
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
                return { background: COLORS[idx % COLORS.length] + '22' };
            }
        }
        return {};
    };

    const Cell = ({ space, style = {} }) => {
        const boardSpace = board?.find(b => b.id === space.id);
        const isVert = style.flexDirection === 'row' || style.flexDirection === 'row-reverse';
        const hasPrice = ['Street', 'Railroad', 'Utility'].includes(space.type);

        return (
            <div
                className="bspace"
                style={{ ...bg(space), ...style, cursor: 'pointer' }}
                title={space.name.replace('\n', ' ')}
                onClick={() => setInspectSpace(space)}
            >
                {space.color && (
                    <div className="bcolor" style={{
                        background: BCOLORS[space.color] || '#ccc',
                        width: isVert ? '12px' : '100%',
                        height: isVert ? '100%' : '12px',
                        minWidth: isVert ? '12px' : 'auto',
                        minHeight: isVert ? 'auto' : '12px'
                    }} />
                )}
                <div className="bname" style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    {space.name}
                </div>
                {hasPrice && (
                    <div style={{
                        fontSize: 'clamp(5px, 0.6vw, 8px)',
                        fontWeight: 700,
                        color: '#333',
                        padding: '2px 3px',
                        flexShrink: 0,
                        background: 'rgba(255,255,255,0.7)',
                        borderRadius: '3px'
                    }}>
                        ${boardSpace?.purchasePrice || '???'}
                    </div>
                )}
                {toks(space.id)}
            </div>
        );
    };

    const InspectModal = () => {
        if (!inspectSpace) return null;

        const boardSpace = board?.find(b => b.id === inspectSpace.id);
        const owner = boardSpace?.ownerId ? players.find(p => p.id === boardSpace.ownerId) : null;

        return (
            <div className="overlay" onClick={() => setInspectSpace(null)}>
                <div className="mbox" onClick={e => e.stopPropagation()}>
                    <div style={{ marginBottom: 16 }}>
                        {inspectSpace.color && (
                            <div style={{
                                background: BCOLORS[inspectSpace.color],
                                height: 40,
                                borderRadius: '8px 8px 0 0',
                                marginBottom: 12
                            }} />
                        )}
                        <h2 style={{ fontSize: 24, marginBottom: 8 }}>{inspectSpace.name.replace('\n', ' ')}</h2>
                        <div style={{ fontSize: 13, color: '#999', marginBottom: 16 }}>
                            {inspectSpace.type} • Position #{inspectSpace.id}
                        </div>
                    </div>

                    {boardSpace && (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                            {boardSpace.purchasePrice && (
                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 14 }}>
                                    <span style={{ color: '#999' }}>Purchase Price</span>
                                    <strong style={{ color: 'var(--green)' }}>${boardSpace.purchasePrice}</strong>
                                </div>
                            )}

                            {owner && (
                                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 14 }}>
                                    <span style={{ color: '#999' }}>Owner</span>
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
                                <span className="badge bg-red" style={{ alignSelf: 'flex-start' }}>Mortgaged</span>
                            )}

                            {boardSpace.houseCount > 0 && (
                                <div style={{ fontSize: 14 }}>
                                    🏠 {boardSpace.houseCount} house{boardSpace.houseCount !== 1 ? 's' : ''}
                                </div>
                            )}

                            {boardSpace.hasHotel && (
                                <div style={{ fontSize: 14 }}>🏨 Hotel</div>
                            )}
                        </div>
                    )}

                    <button
                        className="btn btn-ghost btn-full"
                        style={{ marginTop: 20 }}
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
                {/* Corners - FIXED POSITIONS */}
                {/* GO: bottom-right */}
                <div className="bcorner" style={{ gridColumn: 11, gridRow: 11, fontSize: 8, background: '#e8f5e9' }}>
                    🏁<br />GO{toks(0)}
                </div>
                {/* Jail: bottom-left */}
                <div className="bcorner" style={{ gridColumn: 1, gridRow: 11 }}>
                    ⛓<br />Jail{toks(10)}
                </div>
                {/* Free Parking: top-left */}
                <div className="bcorner" style={{ gridColumn: 1, gridRow: 1 }}>
                    🅿<br />Free{toks(20)}
                </div>
                {/* Go To Jail: top-right */}
                <div className="bcorner" style={{ gridColumn: 11, gridRow: 1 }}>
                    🚔<br />Jail!{toks(30)}
                </div>

                {/* Bottom row: spaces 1-9 (right to left from GO toward Jail) */}
                {SPACES.slice(1, 10).map((s, i) => (
                    <div key={s.id} style={{ gridColumn: 10 - i, gridRow: 11, display: 'flex', flexDirection: 'column' }}>
                        <Cell space={s} style={{ height: '100%', flexDirection: 'column-reverse' }} />
                    </div>
                ))}

                {/* Left column: spaces 11-19 (bottom to top, Jail toward Free Parking) */}
                {SPACES.slice(11, 20).map((s, i) => (
                    <div key={s.id} style={{ gridColumn: 1, gridRow: 10 - i, display: 'flex' }}>
                        <Cell space={s} style={{ height: '100%', width: '100%', flexDirection: 'row' }} />
                    </div>
                ))}

                {/* Top row: spaces 21-29 (left to right from Free Parking to Go To Jail) */}
                {SPACES.slice(21, 30).map((s, i) => (
                    <div key={s.id} style={{ gridColumn: 2 + i, gridRow: 1, display: 'flex', flexDirection: 'column' }}>
                        <Cell space={s} style={{ height: '100%', flexDirection: 'column' }} />
                    </div>
                ))}

                {/* Right column: spaces 31-39 (top to bottom from Go To Jail to GO) */}
                {SPACES.slice(31, 40).map((s, i) => (
                    <div key={s.id} style={{ gridColumn: 11, gridRow: 2 + i, display: 'flex' }}>
                        <Cell space={s} style={{ height: '100%', width: '100%', flexDirection: 'row-reverse' }} />
                    </div>
                ))}

                {/* Center */}
                <div className="bcenter">
                    <div style={{ fontFamily: 'Playfair Display,serif', fontSize: 'clamp(11px,2vw,19px)', fontWeight: 900, textAlign: 'center' }}>
                        MONOPOLY
                    </div>
                    <div style={{ fontSize: 8, color: '#bbb', marginTop: 2 }}>Online Edition</div>
                    <div className="board-card-stacks">
                        <div className="board-card-stack">
                            <div className="card-deck">
                                <div className="card-deck-layer" style={{ background: '#fff9c4', borderColor: '#b8860b44' }} />
                                <div className="card-deck-layer" style={{ background: '#fff9c4', borderColor: '#b8860b66' }} />
                                <div className="card-deck-layer" style={{ background: '#fff9c4', borderColor: '#b8860b99', color: '#b8860b' }}>
                                    ?
                                </div>
                            </div>
                            <div className="board-card-stack-label">Chance</div>
                        </div>
                        <div className="board-card-stack">
                            <div className="card-deck">
                                <div className="card-deck-layer" style={{ background: '#c8e6c9', borderColor: '#2d6a4f44' }} />
                                <div className="card-deck-layer" style={{ background: '#c8e6c9', borderColor: '#2d6a4f66' }} />
                                <div className="card-deck-layer" style={{ background: '#c8e6c9', borderColor: '#2d6a4f99', color: '#2d6a4f' }}>
                                    🏛
                                </div>
                            </div>
                            <div className="board-card-stack-label">Comm. Chest</div>
                        </div>
                    </div>
                </div>
            </div>

            <InspectModal />
        </div>
    );
}