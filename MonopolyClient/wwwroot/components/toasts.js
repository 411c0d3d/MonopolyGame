/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */

// components/toasts.js — depends on constants.js.

/**
 * Fixed-position toast notification tray.
 * @param {{ list: Array<{ id: number, msg: string, type: string }> }} props
 */
function Toasts({list}) {
    return (
        <div className="toast-tray">
            {list.map(t => (
                <div
                    key={t.id}
                    className={`toast${t.type === 'error' ? ' toast-error' : t.type === 'success' ? ' toast-success' : ''}`}
                >
                    {t.type === 'error' ? '⚠' : t.type === 'success' ? '✓' : 'ℹ'} {t.msg}
                </div>
            ))}
        </div>
    );
}