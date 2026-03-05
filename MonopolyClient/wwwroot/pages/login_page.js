/* globals React */

// pages/login_page.js — shown when authService.initialize() finds no authenticated account.

/**
 * Simple login gate. Triggers the Entra External ID redirect flow.
 * @param {{ onLogin: function }} props
 */
function LoginPage({onLogin}) {
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
            <p style={{color: '#666', fontSize: 11, textAlign: 'center', maxWidth: 280}}>
                Powered by Microsoft Entra External ID.
                Your identity is used only to identify you in the game.
            </p>
        </div>
    );
}