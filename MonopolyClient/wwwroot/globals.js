/**
 * @file globals.js
 * IDE-only shim — never loaded in the browser.
 * Declares all UMD globals and server response shapes so the IDE resolves
 * symbols without needing imports. checkJs is disabled in jsconfig.json so
 * no type errors are raised against plain JS files.
 */

// ─── React / ReactDOM UMD globals ────────────────────────────────────────────

const {React} = window;

const {
    useState,
    useEffect,
    useRef,
    useCallback,
    useContext,
    createContext,
} = React;

// ─── SignalR UMD global ───────────────────────────────────────────────────────

/**
 * @typedef {{
 *   Warning:number,
 *   Error:number,
 *   Information:number,
 *   Debug:number,
 *   Trace:number,
 *   None:number
 * }} SignalRLogLevel
 *
 * @typedef {{
 *   withUrl(u:string):SignalRHubConnectionBuilder,
 *   withAutomaticReconnect(r?:number[]):SignalRHubConnectionBuilder,
 *   configureLogging(l:any):SignalRHubConnectionBuilder,
 *   build():SignalRHubConnection
 * }} SignalRHubConnectionBuilder
 *
 * @typedef {{
 *   on(e:string,fn:Function):void,
 *   off(e:string,fn:Function):void,
 *   invoke(m:string,...a:any[]):Promise<any>,
 *   onreconnecting(fn:Function):void,
 *   onreconnected(fn:Function):void,
 *   onclose(fn:Function):void,
 *   start():Promise<void>,
 *   stop():Promise<void>
 * }} SignalRHubConnection
 */

/** @type {{ LogLevel: SignalRLogLevel, HubConnectionBuilder: { new(): SignalRHubConnectionBuilder } }} */
const signalR = window.signalR;

// ─── Server response shapes ───────────────────────────────────────────────────

/**
 * @typedef {{
 *   id:string,
 *   name:string,
 *   cash:number,
 *   position:number,
 *   isConnected:boolean,
 *   isInJail:boolean,
 *   isBankrupt:boolean,
 *   keptCardCount:number,
 *   hasRolledDice:boolean,
 *   propertyCount:number,
 *   isBot:boolean
 * }} Player
 */

/**
 * @typedef {{
 *   id:number,
 *   name:string,
 *   ownerId:string|null,
 *   purchasePrice:number,
 *   isMortgaged:boolean,
 *   houseCount:number,
 *   hasHotel:boolean
 * }} BoardProperty
 */

/**
 * Unified GameState definition (merged, no duplication)
 *
 * @typedef {{
 *   gameId:string,
 *   hostId:string,
 *   hostName:string,
 *   status:string,
 *   players:Player[],
 *   board:BoardProperty[],
 *   currentPlayer:Player|null,
 *   turn:number,
 *   eventLog:string[],
 *   doubleRolled?:boolean,
 *   currentTurnStartedAt:number,
 * }} GameState
 */

/**
 * Game list item (for /api/games)
 *
 * @typedef {{
 *   gameId:string,
 *   hostName:string,
 *   playerCount:number,
 *   maxPlayers:number,
 *   status:string
 * }} GameListItem
 */

/**
 * Admin-specific game details projection
 * Used by AdminPage via SignalR ("GameDetails")
 *
 * @typedef {{
 *   gameId:string,
 *   status:string,
 *   turn:number,
 *   playerCount:number,
 *   players:Player[],
 *   recentLogs:string[]
 * }} AdminGameDetails
 */

/**
 * Trade offer DTO
 *
 * @typedef {{
 *   id:string,
 *   fromPlayerName:string,
 *   offeredCash:number,
 *   requestedCash:number,
 *   offeredPropertyIds:number[],
 *   requestedPropertyIds:number[]
 * }} TradeOffer
 */

// ─── App globals (defined in utils/constants.js) ─────────────────────────────

/** @type {{ createRoot: function(Element): { render: function(*): void } }} */
const ReactDOM = window.ReactDOM;

// ─── App globals (defined in utils/signalr.js) ───────────────────────────────

/**
 * @typedef {{
 *   start():Promise<void>,
 *   on(event:string, fn:Function):function():void,
 *   call(method:string, ...args:any[]):Promise<any>
 * }} IGameHub
 */

/** @type {IGameHub} */
window.gameHub = gameHub;