/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */

// components/header.js — depends on constants.js.

/**
 * Sticky top navigation bar.
 * @param {{ page: string, gameId?: string, me?: string, isAdmin?: boolean, onAdmin?: function, onLeave?: function }} props
 */
function Header({page, gameId, me, isAdmin, onAdmin, onLeave, onBack}) {
    return (
        <div className="header">
            <div
                className="logo-icon"
                onClick={onBack}
                style={{cursor: onBack ? 'pointer' : 'default'}}
            >🎲</div>
            <span
                className="logo-text"
                onClick={onBack}
                style={{cursor: onBack ? 'pointer' : 'default'}}
            >Monopoly</span>
            <div className="header-right">
                {page === 'lobby' && (
                    <span style={{fontSize: 12, color: '#aaa'}}>
                        Lobby ·{' '}
                        <code style={{background: 'var(--cream)', padding: '1px 6px', borderRadius: 4}}>
                            {gameId}
                        </code>
                    </span>
                )}
                {page === 'game' && (
                    <span style={{color: 'var(--green)', fontWeight: 600, fontSize: 12}}>● Live</span>
                )}
                {page === 'admin' && (
                    <span style={{fontSize: 12, color: '#aaa', letterSpacing: 1, textTransform: 'uppercase'}}>
                        ⚙ Admin Panel
                    </span>
                )}
                {me && <div className="hbadge">{me}</div>}
                {isAdmin && page !== 'admin' && (
                    <button className="btn btn-ghost btn-sm" onClick={onAdmin}>⚙ Admin</button>
                )}
                {onLeave && (
                    <button className="btn btn-sm btn-red" onClick={onLeave}>← Leave</button>
                )}
            </div>
        </div>
    );
}