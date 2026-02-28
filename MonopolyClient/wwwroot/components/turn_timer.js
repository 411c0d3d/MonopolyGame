/* globals useState, useEffect */

/**
 * Countdown timer driven by the server-provided turn start timestamp.
 * Color shifts green → amber → red as time runs out.
 * @param {{ startedAt: string|null, limitSeconds: number }} props
 */
function TurnTimer({startedAt, limitSeconds = 120}) {
    const [elapsed, setElapsed] = useState(0);

    useEffect(() => {
        if (!startedAt) {
            setElapsed(0);
            return;
        }

        const tick = () => {
            const diff = Math.floor((Date.now() - new Date(startedAt).getTime()) / 1000);
            setElapsed(Math.min(diff, limitSeconds));
        };

        tick();
        const id = setInterval(tick, 1000);
        return () => clearInterval(id);
    }, [startedAt, limitSeconds]);

    const remaining = Math.max(limitSeconds - elapsed, 0);
    const mins = String(Math.floor(remaining / 60)).padStart(2, '0');
    const secs = String(remaining % 60).padStart(2, '0');
    const ratio = remaining / limitSeconds;
    const color = ratio > 0.5 ? 'var(--green)' : ratio > 0.25 ? '#f5a623' : 'var(--red)';

    return (
        <span style={{fontVariantNumeric: 'tabular-nums', color, fontWeight: 700, fontSize: 12}}>
            {mins}:{secs}
        </span>
    );
}