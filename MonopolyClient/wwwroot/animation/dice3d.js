/* globals useState, useEffect, useRef, useCallback, React */

// dice3d.js — 3D CSS die components and useDiceRoll hook.
// Replaces the dice exports from animations.js.
// DiceTray accepts the same { dice, rolling } props as before — no changes
// needed in game_page.js.
// One board.js change required: replace the dice conditional block
// (the DiceTray ternary + placeholder) with simply <DiceTray dice={dice} rolling={rolling}/>

// ─── Face rotation map ────────────────────────────────────────────────────────
// Each entry is the cube rotation (rx, ry in degrees) that brings that face
// value to face the viewer.  Derived from the inverse of each face's CSS
// positioning transform.
//
//   Front  (translateZ)            → value 1 → cube { rx:   0, ry:   0 }
//   Top    (rotateX(-90) translateZ)→ value 2 → cube { rx:  90, ry:   0 }
//   Right  (rotateY( 90) translateZ)→ value 3 → cube { rx:   0, ry: -90 }
//   Left   (rotateY(-90) translateZ)→ value 4 → cube { rx:   0, ry:  90 }
//   Bottom (rotateX( 90) translateZ)→ value 5 → cube { rx: -90, ry:   0 }
//   Back   (rotateY(180) translateZ)→ value 6 → cube { rx:   0, ry: 180 }

/** Cube rotations that bring each die face value to face the viewer. */
const FACE_ROTS = {
    1: { rx: 0,   ry: 0   },
    2: { rx: 90,  ry: 0   },
    3: { rx: 0,   ry: -90 },
    4: { rx: 0,   ry: 90  },
    5: { rx: -90, ry: 0   },
    6: { rx: 0,   ry: 180 },
};

// ─── Pip positions ────────────────────────────────────────────────────────────
// Each entry: array of [left%, top%] positions for each pip on that face value.
const PIPS = {
    1: [                [50, 50]                                            ],
    2: [ [26, 26],                              [74, 74]                   ],
    3: [ [26, 26],      [50, 50],               [74, 74]                   ],
    4: [ [26, 26],   [74, 26],       [26, 74],  [74, 74]                   ],
    5: [ [26, 26],   [74, 26],       [50, 50],  [26, 74],  [74, 74]        ],
    6: [ [26, 22],   [74, 22],  [26, 50],  [74, 50],  [26, 78],  [74, 78] ],
};

/** Value assigned to each named face — must match FACE_ROTS above. */
const FACE_VALUES = { front: 1, top: 2, right: 3, left: 4, bottom: 5, back: 6 };
const FACE_NAMES  = ['front', 'back', 'right', 'left', 'top', 'bottom'];

/** Idle resting tilt shown when no roll has taken place yet. */
const IDLE_ROT = { rx: 18, ry: -24, rz: 0, t: 'none' };

// ─── DiePips ──────────────────────────────────────────────────────────────────

/** Renders pip dots for a single die face value. */
function DiePips({ value }) {
    const positions = PIPS[value] || [];
    return (
        <div className="die3d-pips">
            {positions.map(([l, t], i) => (
                <div key={i} className="die3d-pip" style={{ left: `${l}%`, top: `${t}%` }}/>
            ))}
        </div>
    );
}

// ─── Die3D ────────────────────────────────────────────────────────────────────

/**
 * Single 3D CSS die cube.
 * Receives an explicit rotation descriptor and a bouncing flag.
 * @param {{ rotation: {rx,ry,rz,t}, bouncing: boolean }} props
 */
function Die3D({ rotation, bouncing = false }) {
    const { rx = 0, ry = 0, rz = 0, t = 'none' } = rotation || {};

    return (
        <div className={`die3d-scene${bouncing ? ' die3d-bouncing' : ''}`}>
            <div
                className="die3d-cube"
                style={{
                    transform: `rotateX(${rx}deg) rotateY(${ry}deg) rotateZ(${rz}deg)`,
                    transition: t,
                }}
            >
                {FACE_NAMES.map(face => (
                    <div key={face} className={`die3d-face die3d-face--${face}`}>
                        <DiePips value={FACE_VALUES[face]}/>
                    </div>
                ))}
            </div>
            <div className="die3d-shadow"/>
        </div>
    );
}

// ─── DiceTray ─────────────────────────────────────────────────────────────────

/**
 * Renders two 3D dice in a tray.
 * Manages all rotation state internally across three animation phases:
 *   Phase 1 (rolling=true):  large multi-revolution spin.
 *   Phase 2 (rolling→false): snap to correct face with eased settle.
 *   Phase 3:                 micro-bounce on landing.
 * Identical external API to the previous flat DiceTray.
 * @param {{ dice: number[], rolling: boolean }} props
 */
function DiceTray({ dice = [], rolling = false }) {
    const [rots,     setRots    ] = useState([IDLE_ROT, IDLE_ROT]);
    const [bouncing, setBouncing] = useState([false, false]);
    // Ref tracks the last applied target rotation so snap can compute
    // the nearest full-revolution offset without referencing CSS state.
    const lastRots = useRef([IDLE_ROT, IDLE_ROT]);

    // Phase 1 — start spinning on roll trigger
    useEffect(() => {
        if (!rolling) { return; }
        setRots(prev => {
            const next = prev.map(r => ({
                rx: r.rx + 720 + Math.random() * 360,
                ry: r.ry + 720 + Math.random() * 360,
                rz: (Math.random() - 0.5) * 22,
                t:  'transform 0.78s cubic-bezier(0.15, 0.85, 0.3, 1)',
            }));
            lastRots.current = next;
            return next;
        });
    }, [rolling]);

    // Phase 2 + 3 — snap to face on settle, then bounce
    useEffect(() => {
        if (rolling || !dice[0] || !dice[1]) { return; }

        const snapped = dice.map((v, i) => {
            const base = FACE_ROTS[v] || { rx: 0, ry: 0 };
            const cur  = lastRots.current[i] || { rx: 0, ry: 0 };
            // Find nearest full revolution for each axis independently,
            // then add the base offset — minimises final travel distance.
            const rx = Math.round((cur.rx - base.rx) / 360) * 360 + base.rx;
            const ry = Math.round((cur.ry - base.ry) / 360) * 360 + base.ry;
            return { rx, ry, rz: 0, t: 'transform 0.32s cubic-bezier(0.34, 1.0, 0.64, 1)' };
        });
        lastRots.current = snapped;
        setRots(snapped);

        // Delay bounce to after snap transition completes
        const snapDuration = 320;
        const bounceDuration = 340;
        const startBounce = setTimeout(() => {
            setBouncing([true, true]);
            const stopBounce = setTimeout(() => { setBouncing([false, false]); }, bounceDuration);
            return () => { clearTimeout(stopBounce); };
        }, snapDuration);

        return () => { clearTimeout(startBounce); };
    }, [rolling, dice]);

    const hasValues = !!(dice[0] && dice[1]);
    const isDoubles = hasValues && dice[0] === dice[1];
    const sum       = hasValues ? dice[0] + dice[1] : null;

    return (
        <div className="darea" style={{ opacity: hasValues || rolling ? 1 : 0.35 }}>

            {/* Dice row with sum label */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <Die3D rotation={rots[0]} bouncing={bouncing[0]}/>
                <Die3D rotation={rots[1]} bouncing={bouncing[1]}/>
                <div style={{
                    fontSize:   'clamp(10px, 2cqw, 16px)',
                    fontWeight: 700,
                    minWidth:   '2em',
                    textAlign:  'center',
                    color:      isDoubles ? 'var(--gold)' : 'rgba(255,255,255,0.7)',
                    opacity:    hasValues ? 1 : 0,
                    transition: 'opacity 0.2s',
                }}>
                    {sum !== null ? `\u2003:\u2003${sum}` : ''}
                </div>
            </div>

            {/* Doubles badge row — reserved height prevents jitter */}
            <div className="board-dice-doubles-slot">
                {isDoubles && (
                    <span className="doubles-badge">🎲 Doubles!</span>
                )}
            </div>
        </div>
    );
}

// ─── useDiceRoll ──────────────────────────────────────────────────────────────

/**
 * Hook managing dice roll state for the game page.
 * API is identical to the animations.js version: [dice, rolling, triggerRoll, settleDice].
 * DiceTray now derives all visual rotation state from these props internally.
 */
function useDiceRoll() {
    const [dice,    setDice   ] = useState([null, null]);
    const [rolling, setRolling] = useState(false);

    /** Clears current dice and marks rolling=true; call before the hub RollDice invocation. */
    const triggerRoll = useCallback(() => {
        setDice([null, null]);
        setRolling(true);
    }, []);

    /** Called with [d1, d2] from the DiceRolled SignalR event. */
    const settleDice = useCallback((values) => {
        setDice(values);
        setRolling(false);
    }, []);

    return [dice, rolling, triggerRoll, settleDice];
}