/* globals msal, MSAL_CLIENT_ID, MSAL_AUTHORITY, MSAL_SCOPE */

// utils/auth_service.js — MSAL authentication wrapper for Microsoft Entra External ID.
// Handles login, logout, silent token renewal, admin role detection, session restore, and guest mode.

const _scopes = ['openid', 'profile', 'email', MSAL_SCOPE];

/** Key used to persist last active game across page refreshes. */
const SESSION_GAME_KEY = 'monopoly_session_game';

/** Key used to persist guest identity across page refreshes. */
const SESSION_GUEST_KEY = 'monopoly_guest';

class AuthService {
    constructor() {
        this._account = null;
        this._app     = null;
        this._guest   = null; // { objectId, name }
    }

    /** Builds the MSAL config — called lazily so window.msal is guaranteed present. */
    _getApp() {
        if (this._app) {
            return this._app;
        }
        this._app = new window.msal.PublicClientApplication({
            auth: {
                clientId: MSAL_CLIENT_ID,
                authority: MSAL_AUTHORITY,
                redirectUri: window.location.origin,
                postLogoutRedirectUri: window.location.origin,
            },
            cache: {
                cacheLocation: 'sessionStorage',
                storeAuthStateInCookie: false,
            },
        });
        return this._app;
    }

    /**
     * Must be called once on app startup.
     * Handles the post-redirect callback, restores the cached MSAL account, or restores a guest session.
     * Returns the account/guest object if authenticated, null otherwise.
     */
    async initialize() {
        // Restore guest session before touching MSAL (avoids unnecessary network call).
        const storedGuest = sessionStorage.getItem(SESSION_GUEST_KEY);
        if (storedGuest) {
            try {
                this._guest = JSON.parse(storedGuest);
                return this._guest;
            } catch {
                sessionStorage.removeItem(SESSION_GUEST_KEY);
            }
        }

        const app = this._getApp();
        await app.initialize();

        const result = await app.handleRedirectPromise();
        if (result?.account) {
            app.setActiveAccount(result.account);
            this._account = result.account;
        } else {
            const accounts = app.getAllAccounts();
            if (accounts.length > 0) {
                app.setActiveAccount(accounts[0]);
                this._account = accounts[0];
            }
        }

        return this._account;
    }

    /** Returns true if the user has an active authenticated session (MSAL or guest). */
    isAuthenticated() {
        return !!this._account || !!this._guest;
    }

    /** Returns true when running in guest mode (no real identity). */
    isGuest() {
        return !!this._guest;
    }

    /**
     * Returns the current user's identity from the cached account or guest session.
     * objectId is used as Player.Id server-side.
     */
    getUser() {
        if (this._guest) {
            return {
                objectId: this._guest.objectId,
                name: this._guest.name,
                email: null,
            };
        }
        if (!this._account) {
            return null;
        }
        return {
            objectId: this._account.localAccountId,
            name: this._account.name || this._account.username.split('@')[0],
            email: this._account.username,
        };
    }

    /**
     * Acquires an access token silently, falling back to a popup if interaction is required.
     * Returns null for guest sessions — the hub connection will be unauthenticated.
     * NOTE: GameHub must allow anonymous connections for guest play to work server-side.
     */
    async getToken() {
        if (this._guest) {
            return null;
        }
        if (!this._account) {
            return null;
        }
        const app = this._getApp();
        try {
            const result = await app.acquireTokenSilent({scopes: _scopes, account: this._account});
            return result.accessToken;
        } catch (err) {
            if (err instanceof window.msal.InteractionRequiredAuthError) {
                try {
                    const result = await app.acquireTokenPopup({scopes: _scopes});
                    this._account = result.account;
                    app.setActiveAccount(result.account);
                    return result.accessToken;
                } catch {
                    return null;
                }
            }
            return null;
        }
    }

    /**
     * Checks admin status by decoding the JWT roles claim.
     * Always returns false for guests.
     */
    async isAdmin() {
        if (this._guest) {
            return false;
        }
        const token = await this.getToken();
        if (!token) {
            return false;
        }
        try {
            const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
            const roles = payload.roles ?? [];
            if (Array.isArray(roles) ? roles.includes('Admin') : roles === 'Admin') {
                return true;
            }
        } catch {
            // ignore decode errors, fall through to server check
        }
        try {
            const res = await fetch(`${SERVER_URL}/api/me`, {
                headers: {Authorization: `Bearer ${token}`},
            });
            if (!res.ok) {
                return false;
            }
            const data = await res.json();
            return data.isAdmin === true;
        } catch {
            return false;
        }
    }

    /** Triggers the Entra External ID login redirect flow. */
    login() {
        return this._getApp().loginRedirect({scopes: _scopes, prompt: 'login'});
    }

    /**
     * Starts a guest session with the given display name.
     * Generates a stable guest ID prefixed with "guest_" so the server can identify guests.
     * NOTE: The server's GameHub must support anonymous/guest connections for this to work.
     */
    loginAsGuest(name) {
        const trimmed = (name || '').trim();
        if (!trimmed) {
            throw new Error('A display name is required to play as guest.');
        }
        const objectId = 'guest_' + crypto.randomUUID();
        this._guest = {objectId, name: trimmed};
        sessionStorage.setItem(SESSION_GUEST_KEY, JSON.stringify(this._guest));
    }

    /** Clears guest session without touching MSAL state. */
    clearGuest() {
        this._guest = null;
        sessionStorage.removeItem(SESSION_GUEST_KEY);
    }

    /** Logs out (MSAL or guest) and clears all session state. */
    logout() {
        this.clearSessionGame();
        if (this._guest) {
            this.clearGuest();
            sessionStorage.clear();
            window.location.reload();
            return Promise.resolve();
        }
        sessionStorage.clear();
        return this._getApp().logoutRedirect({account: this._account});
    }

    // -------------------------------------------------------------------------
    // Session game persistence — survives page refresh
    // -------------------------------------------------------------------------

    /** Saves the active game ID to sessionStorage so it survives a page refresh. */
    saveSessionGame(gameId) {
        sessionStorage.setItem(SESSION_GAME_KEY, gameId);
    }

    /** Returns the last active game ID, or null if none. */
    getSessionGame() {
        return sessionStorage.getItem(SESSION_GAME_KEY);
    }

    /** Clears the persisted game session (on leave or logout). */
    clearSessionGame() {
        sessionStorage.removeItem(SESSION_GAME_KEY);
    }
}

const authService = new AuthService();
window.authService = authService;