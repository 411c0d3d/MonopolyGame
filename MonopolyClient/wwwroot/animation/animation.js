/* globals useState, useEffect, useRef, useCallback, React */

// animations.js — Game animation utilities: player hop, dice roll, chest card popup.
// Depends on: React hooks available globally via globals.js.

// ─── Constants ────────────────────────────────────────────────────────────────

const BOARD_SIZE = 40;
const HOP_INTERVAL_MS = 180;   // ms per cell hop
const CARD_POPUP_MS = 5000;    // ms before card popup auto-dismisses

/** Pip grid positions per die face value (row, col in a 3×3 grid). */
const DIE_PIPS = {
    1: [[1, 1]],
    2: [[0, 0], [2, 2]],
    3: [[0, 0], [1, 1], [2, 2]],
    4: [[0, 0], [0, 2], [2, 0], [2, 2]],
    5: [[0, 0], [0, 2], [1, 1], [2, 0], [2, 2]],
    6: [[0, 0], [0, 2], [1, 0], [1, 2], [2, 0], [2, 2]],
};

/** Card type display config: icon and accent colour. */
const CARD_DISPLAY = {
    Chance: {icon: '❓', color: '#f59e0b', label: 'Chance'},
    CommunityChest: {icon: '📦', color: '#3b82f6', label: 'Community Chest'},
    Tax: {icon: '💸', color: '#ef4444', label: 'Tax'},
    GoToJail: {icon: '⛓', color: '#6b7280', label: 'Go To Jail'},
    Go: {icon: '🚀', color: '#10b981', label: 'GO'},
    FreeParking: {icon: '🅿', color: '#8b5cf6', label: 'Free Parking'},
    Jail: {icon: '⛓', color: '#6b7280', label: 'Just Visiting'},
    Railroad: {icon: '🚂', color: '#374151', label: 'Railroad'},
    Utility: {icon: '⚡', color: '#f59e0b', label: 'Utility'},
    default: {icon: '📋', color: '#6b7280', label: 'Card'},
};

// ─── Hooks ────────────────────────────────────────────────────────────────────

/**
 * Tracks each player's last-known position and yields intermediate cell indices
 * so the Board can animate token movement hop-by-hop.
 * @param {any[]} players - Array of player objects with { id, position }.
 * @returns {Object} Map of playerId → current display position (number).
 */
function usePlayerHop(players) {
    const [animPositions, setAnimPositions] = useState(() => {
        const init = {};
        players.forEach(p => {
            init[p.id] = p.position;
        });
        return init;
    });

    const prevPositions = useRef({});
    const timersRef = useRef({});   // pending timeouts per player

    useEffect(() => {
        players.forEach(player => {
            const prev = prevPositions.current[player.id];
            const curr = player.position;

            // First render or no change
            if (prev === undefined) {
                prevPositions.current[player.id] = curr;
                setAnimPositions(ap => ({...ap, [player.id]: curr}));
                return;
            }

            if (prev === curr) {
                return;
            }

            // Clear any in-progress animation for this player
            (timersRef.current[player.id] || []).forEach(clearTimeout);
            timersRef.current[player.id] = [];

            // Build ordered path from prev → curr (wraps around board)
            const path = [];
            let pos = prev;
            while (pos !== curr) {
                pos = (pos + 1) % BOARD_SIZE;
                path.push(pos);
            }

            // Schedule each cell hop
            path.forEach((cellPos, i) => {
                const t = setTimeout(() => {
                    setAnimPositions(ap => ({...ap, [player.id]: cellPos}));
                }, i * HOP_INTERVAL_MS);
                timersRef.current[player.id].push(t);
            });

            prevPositions.current[player.id] = curr;
        });
    }, [players]);

    // Cleanup all timers on unmount
    useEffect(() => {
        return () => {
            Object.values(timersRef.current).forEach(timers => {
                timers.forEach(clearTimeout);
            });
        };
    }, []);

    return animPositions;
}

/**
 * Auto-dismissing card draw popup shown for CARD_POPUP_MS milliseconds.
 * @param {{ card: { type: string, text: string, amount?: number }|null, onDismiss: Function }} props
 */
function ChestCardPopup({card, onDismiss}) {
    const progressRef = useRef(null);
    const timerRef = useRef(null);
    const startRef = useRef(null);

    useEffect(() => {
        if (!card) {
            return;
        }

        startRef.current = performance.now();

        // Animate progress bar via rAF so it's smooth regardless of re-renders
        const tick = (now) => {
            const elapsed = now - startRef.current;
            const fraction = Math.min(elapsed / CARD_POPUP_MS, 1);
            if (progressRef.current) {
                progressRef.current.style.transform = `scaleX(${1 - fraction})`;
            }
            if (fraction < 1) {
                requestAnimationFrame(tick);
            }
        };
        requestAnimationFrame(tick);

        timerRef.current = setTimeout(onDismiss, CARD_POPUP_MS);
        return () => clearTimeout(timerRef.current);
    }, [card]);

    if (!card) {
        return null;
    }

    const display = CARD_DISPLAY[card.type] || CARD_DISPLAY.default;

    return (
        <div className="card-popup-overlay" onClick={onDismiss} role="dialog" aria-modal="true">
            <div
                className="card-popup"
                onClick={e => e.stopPropagation()}
                style={{'--card-accent': display.color}}
            >
                {/* Progress bar */}
                <div className="card-popup-progress">
                    <div className="card-popup-progress-bar" ref={progressRef}/>
                </div>

                {/* Type header */}
                <div className="card-popup-header">
                    <span className="card-popup-type">{display.label}</span>
                </div>

                {/* Icon */}
                <div className="card-popup-icon">{display.icon}</div>

                {/* Card text */}
                <div className="card-popup-text">{card.text}</div>

                {/* Amount badge if applicable */}
                {card.amount != null && (
                    <div className={`card-popup-amount ${card.amount >= 0 ? 'positive' : 'negative'}`}>
                        {card.amount >= 0 ? '+' : ''}${Math.abs(card.amount).toLocaleString()}
                    </div>
                )}

                <button className="card-popup-dismiss" onClick={onDismiss}>
                    Dismiss
                </button>
            </div>
        </div>
    );
}

// Styles live in animations.css — link that stylesheet in your HTML before this script.