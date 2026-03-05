/* globals signalR, SERVER_URL, authService */

// utils/hub_service.js

class HubService {
    constructor() {
        this._conn = null;
        this._handlers = {};
    }

    /**
     * Builds and starts the SignalR connection.
     * accessTokenFactory ensures every WebSocket upgrade and reconnect
     * sends a fresh JWT as ?access_token= — required because browsers
     * cannot set Authorization headers on WebSocket connections.
     */
    start() {
        this._conn = new signalR.HubConnectionBuilder()
            .withUrl(`${SERVER_URL}/game-hub`, {
                accessTokenFactory: () => authService.getToken(),
            })
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        const events = [
            'GameStateUpdated',
            'Error',
            'TradeProposed',
            'GameForceEnded',
            'GamePaused',
            'GameResumed',
            'PlayerKicked',
            'Kicked',
            'TurnWarning',
            'GameCreated',
            'GameRemoved',
            'DiceRolled',
            'CardDrawn',
        ];

        events.forEach(evt => {
            this._conn.on(evt, (...args) => this._fire(evt, ...args));
        });

        this._conn.onreconnecting(() => this._fire('Reconnecting'));
        this._conn.onreconnected(() => this._fire('Reconnected'));
        this._conn.onclose(() => this._fire('Closed'));

        return this._conn.start();
    }

    /** Subscribes to a hub event. Returns an unsubscribe function. */
    on(event, fn) {
        if (!this._handlers[event]) { this._handlers[event] = []; }
        this._handlers[event].push(fn);
        return () => {
            this._handlers[event] = this._handlers[event].filter(f => f !== fn);
        };
    }

    /** Invokes a hub method. Returns a Promise. */
    call(method, ...args) {
        if (!this._conn) { return Promise.reject('Not connected'); }
        return this._conn.invoke(method, ...args);
    }

    _fire(event, ...args) {
        (this._handlers[event] || []).forEach(fn => fn(...args));
    }
}

const gameHub = new HubService();
window.gameHub = gameHub;