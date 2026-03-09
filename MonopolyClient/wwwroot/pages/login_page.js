/* globals React, useState */

// pages/login_page.js — shown when authService.initialize() finds no authenticated account.

/**
 * Login gate. Triggers the Entra External ID redirect flow or starts a guest session.
 * @param {{ onLogin: function, onGuest: function(name: string) }} props
 */
function LoginPage({onLogin, onGuest}) {
    const [showGuest, setShowGuest] = useState(false);
    const [guestName, setGuestName] = useState('');
    const [nameError, setNameError] = useState('');

    const handleGuestSubmit = () => {
        const trimmed = guestName.trim();
        if (!trimmed) {
            setNameError('Please enter a display name.');
            return;
        }
        if (trimmed.length > 24) {
            setNameError('Name must be 24 characters or fewer.');
            return;
        }
        setNameError('');
        onGuest(trimmed);
    };

    const handleGuestKeyDown = (e) => {
        if (e.key === 'Enter') { handleGuestSubmit(); }
    };

    return (
        <div className="cover" style={{flexDirection: 'column', gap: 24}}>
            <div style={{textAlign: 'center'}}>
                <div style={{fontSize: 52, marginBottom: 12}}>🎲</div>
                <h1 className="htitle" style={{marginBottom: 6}}>
                    Play <span>Monopoly</span> Online
                </h1>
                <p style={{color: '#aaa', fontSize: 13}}>Sign in to create or join a game</p>
            </div>

            <button
                className="btn btn-green btn-lg"
                style={{minWidth: 220}}
                onClick={onLogin}
            >
                Sign in with Google or Microsoft
            </button>

            {!showGuest ? (
                <button
                    className="btn btn-ghost btn-lg"
                    style={{minWidth: 220}}
                    onClick={() => setShowGuest(true)}
                >
                    🎭 Play as Guest
                </button>
            ) : (
                <div style={{display: 'flex', flexDirection: 'column', gap: 8, minWidth: 220}}>
                    <input
                        className="input"
                        type="text"
                        placeholder="Your display name"
                        maxLength={24}
                        value={guestName}
                        autoFocus
                        onChange={e => { setGuestName(e.target.value); setNameError(''); }}
                        onKeyDown={handleGuestKeyDown}
                    />
                    {nameError && (
                        <div style={{fontSize: 11, color: 'var(--red)'}}>{nameError}</div>
                    )}
                    <button className="btn btn-ghost btn-full" onClick={handleGuestSubmit}>
                        Continue as Guest →
                    </button>
                    <button
                        className="btn btn-ghost btn-full"
                        style={{color: '#aaa', fontSize: 12}}
                        onClick={() => { setShowGuest(false); setNameError(''); }}
                    >
                        ← Back
                    </button>
                </div>
            )}

            <p style={{color: '#666', fontSize: 11, textAlign: 'center', maxWidth: 280}}>
                Powered by Microsoft Entra External ID.
                Your identity is used only to identify you in the game.
            </p>
        </div>
    );
}