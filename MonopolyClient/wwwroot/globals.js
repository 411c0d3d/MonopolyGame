/**
 * @file globals.js
 * IDE-only shim — never loaded in the browser.
 * Declares all UMD globals and server response shapes so the IDE resolves
 * symbols without needing imports. checkJs is disabled in jsconfig.json so
 * no type errors are raised against plain JS files.
 */

// ─── React / ReactDOM UMD globals ────────────────────────────────────────────

const { React } = window;

const {
    useState,
    useEffect,
    useRef,
    useCallback,
    useContext,
    useReducer,
    useMemo,
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
 *   withUrl(u:string, options?:object):SignalRHubConnectionBuilder,
 *   withAutomaticReconnect(r?:number[]):SignalRHubConnectionBuilder,
 *   configureLogging(l:any):SignalRHubConnectionBuilder,
 *   build():SignalRHubConnection
 * }} SignalRHubConnectionBuilder
 *
 * @typedef {{
 *   on(e:string, fn:Function):void,
 *   off(e:string, fn:Function):void,
 *   invoke(m:string, ...a:any[]):Promise<any>,
 *   onreconnecting(fn:Function):void,
 *   onreconnected(fn:Function):void,
 *   onclose(fn:Function):void,
 *   start():Promise<void>,
 *   stop():Promise<void>
 * }} SignalRHubConnection
 */

/** @type {{ LogLevel:SignalRLogLevel, HubConnectionState:any, HubConnectionBuilder:{ new():SignalRHubConnectionBuilder } }} */
const signalR = window.signalR;

// ─── MSAL UMD global ─────────────────────────────────────────────────────────

/**
 * @typedef {{
 *   initialize():Promise<void>,
 *   handleRedirectPromise():Promise<{account:MsalAccount}|null>,
 *   getAllAccounts():MsalAccount[],
 *   setActiveAccount(a:MsalAccount):void,
 *   acquireTokenSilent(r:object):Promise<{accessToken:string}>,
 *   acquireTokenPopup(r:object):Promise<{accessToken:string, account:MsalAccount}>,
 *   loginRedirect(r:object):Promise<void>,
 *   logoutRedirect(r:object):Promise<void>
 * }} MsalPublicClientApplication
 *
 * @typedef {{
 *   localAccountId:string,
 *   name:string,
 *   username:string
 * }} MsalAccount
 */

/** @type {{ PublicClientApplication:{ new(config:object):MsalPublicClientApplication }, InteractionRequiredAuthError:{ new(...a:any[]):Error } }} */
const msal = window.msal;

// ─── Server response shapes ───────────────────────────────────────────────────

/**
 * @typedef {{
 *   id:string,
 *   name:string,
 *   cash:number,
 *   position:number,
 *   colorIndex:number,
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
 *   group:string,
 *   ownerId:string|null,
 *   purchasePrice:number,
 *   isMortgaged:boolean,
 *   houseCount:number,
 *   hasHotel:boolean,
 *   houseCost:number,
 *   boardSpaces:number
 * }} BoardProperty
 */

/**
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
 *   lastDice?:number[],
 *   currentTurnStartedAt:number
 * }} GameState
 */

/**
 * @typedef {{
 *   gameId:string,
 *   hostName:string,
 *   playerCount:number,
 *   maxPlayers:number,
 *   status:string
 * }} GameListItem
 */

/**
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
 * @typedef {{
 *   id:string,
 *   fromPlayerId:string,
 *   fromPlayerName:string,
 *   toPlayerId:string,
 *   offeredCash:number,
 *   requestedCash:number,
 *   offeredPropertyIds:number[],
 *   requestedPropertyIds:number[]
 * }} TradeOffer
 */

// ─── App globals (defined in utils/constants.js) ─────────────────────────────

/** @type {{ createRoot:function(Element):{ render:function(*):void } }} */
const ReactDOM = window.ReactDOM;

/** @type {any} */
const Ctx = window.Ctx;

/** @type {string} */
const SERVER_URL = window.SERVER_URL;

/** @type {string} */
const MSAL_CLIENT_ID = window.MSAL_CLIENT_ID;

/** @type {string} */
const MSAL_AUTHORITY = window.MSAL_AUTHORITY;

/** @type {string} */
const MSAL_SCOPE = window.MSAL_SCOPE;

/** @type {string[]} */
const COLORS = window.COLORS;

/** @type {string[]} */
const BCOLORS = window.BCOLORS;

/** @type {object} */
const SPACES = window.SPACES;

// ─── App globals (defined in utils/hub_service.js and utils/auth_service.js) ─

/**
 * @typedef {{
 *   start():Promise<void>,
 *   on(event:string, fn:Function):function():void,
 *   call(method:string, ...args:any[]):Promise<any>
 * }} IGameHub
 */

/** @type {IGameHub} */
const gameHub = window.gameHub;

/**
 * @typedef {{
 *   initialize():Promise<MsalAccount|null>,
 *   isAuthenticated():boolean,
 *   getUser():{ objectId:string, name:string, email:string }|null,
 *   getToken():Promise<string|null>,
 *   isAdmin():Promise<boolean>,
 *   login():Promise<void>,
 *   logout():Promise<void>,
 *   saveSessionGame(gameId:string):void,
 *   getSessionGame():string|null,
 *   clearSessionGame():void
 * }} IAuthService
 */

/** @type {IAuthService} */
let authService = window.authService;