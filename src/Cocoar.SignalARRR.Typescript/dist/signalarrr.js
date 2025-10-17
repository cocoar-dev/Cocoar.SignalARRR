import * as signalR from "@microsoft/signalr";
import { ClientRequestMessage } from './client-request-message';
import { ClientResponseMessage } from "./client-response-message";
import { HARRRConnectionOptions } from "./harrr-connection-options";
export class HARRRConnection {
    constructor(hubConnection, options) {
        var _a, _b, _c, _d;
        this._accessTokenFactory = () => "";
        this._serverRequestHandlers = new Map();
        this._hubConnection = hubConnection;
        this._options = options !== null && options !== void 0 ? options : new HARRRConnectionOptions();
        this._accessTokenFactory = (_c = (_b = (_a = this._hubConnection.connection) === null || _a === void 0 ? void 0 : _a._options) === null || _b === void 0 ? void 0 : _b.accessTokenFactory) !== null && _c !== void 0 ? _c : (_d = this._hubConnection.connection) === null || _d === void 0 ? void 0 : _d._accessTokenFactory;
        this._hubConnection.on("ChallengeAuthentication", (request) => {
            const msg = new ClientResponseMessage(request.Id);
            msg.Payload = this._accessTokenFactory();
            this._hubConnection.send("ReplyServerRequest", msg);
        });
        this._hubConnection.on("InvokeServerRequest", (request) => {
            const msg = new ClientResponseMessage(request.Id);
            const handler = this._serverRequestHandlers.get(request.Method);
            if (handler) {
                var result;
                if (request.Arguments) {
                    result = handler(request.Arguments);
                }
                else {
                    result = handler();
                }
                msg.Payload = result;
                if (this._options.HttpResponse) {
                    const payload = JSON.stringify(msg);
                    var url = "";
                    var _headers = {
                        'Content-Type': 'application/json; charset=UTF-8',
                        'Authorization': this._accessTokenFactory()
                    };
                    fetch(url, {
                        method: 'POST',
                        body: payload,
                        headers: _headers
                    });
                }
                else {
                    this._hubConnection.send("ReplyServerRequest", msg);
                }
            }
        });
    }
    /** Indicates the url of the {@link HubConnection} to the server. */
    get baseUrl() {
        return this._hubConnection.baseUrl;
    }
    /**
     * Sets a new url for the HubConnection. Note that the url can only be changed when the connection is in either the Disconnected or
     * Reconnecting states.
     * @param {string} url The url to connect to.
     */
    set baseUrl(value) {
        this._hubConnection.baseUrl = value;
    }
    /** Represents the connection id of the {@link HubConnection} on the server. The connection id will be null when the connection is either
     *  in the disconnected state or if the negotiation step was skipped.
     */
    get connectionId() {
        return this._hubConnection.connectionId;
    }
    /** Indicates the state of the {@link HubConnection} to the server. */
    get state() {
        return this._hubConnection.state;
    }
    /** The server timeout in milliseconds.
     *
     * If this timeout elapses without receiving any messages from the server, the connection will be terminated with an error.
     * The default timeout value is 30,000 milliseconds (30 seconds).
     */
    get serverTimeoutInMilliseconds() {
        return this._hubConnection.serverTimeoutInMilliseconds;
    }
    set serverTimeoutInMilliseconds(value) {
        this._hubConnection.serverTimeoutInMilliseconds = value;
    }
    /** Default interval at which to ping the server.
     *
     * The default value is 15,000 milliseconds (15 seconds).
     * Allows the server to detect hard disconnects (like when a client unplugs their computer).
     */
    get keepAliveIntervalInMilliseconds() {
        return this._hubConnection.keepAliveIntervalInMilliseconds;
    }
    set keepAliveIntervalInMilliseconds(value) {
        this._hubConnection.keepAliveIntervalInMilliseconds = value;
    }
    /** Starts the connection.
     *
     * @returns {Promise<void>} A Promise that resolves when the connection has been successfully established, or rejects with an error.
     */
    start() {
        return this._hubConnection.start();
    }
    /** Stops the connection.
     *
     * @returns {Promise<void>} A Promise that resolves when the connection has been successfully terminated, or rejects with an error.
     */
    stop() {
        return this._hubConnection.stop();
    }
    /** Registers a handler that will be invoked when the connection is closed.
     *
     * @param {Function} callback The handler that will be invoked when the connection is closed. Optionally receives a single argument containing the error that caused the connection to close (if any).
     */
    onClose(callback) {
        this._hubConnection.onclose(callback);
    }
    /** Registers a handler that will be invoked when the connection starts reconnecting.
     *
     * @param {Function} callback The handler that will be invoked when the connection starts reconnecting. Optionally receives a single argument containing the error that caused the connection to start reconnecting (if any).
     */
    onReconnecting(callback) {
        this._hubConnection.onreconnecting(callback);
    }
    /** Registers a handler that will be invoked when the connection successfully reconnects.
     *
     * @param {Function} callback The handler that will be invoked when the connection successfully reconnects.
     */
    onReconnected(callback) {
        this._hubConnection.onreconnected(callback);
    }
    /** Invokes a hub method on the server using the specified name and arguments.
     *
     * The Promise returned by this method resolves when the server indicates it has finished invoking the method. When the promise
     * resolves, the server has finished invoking the method. If the server method returns a result, it is produced as the result of
     * resolving the Promise.
     *
     * @typeparam T The expected return type.
     * @param {string} methodName The name of the server method to invoke.
     * @param {any[]} args The arguments used to invoke the server method.
     * @returns {Promise<T>} A Promise that resolves with the result of the server method (if any), or rejects with an error.
     */
    invoke(methodName, ...args) {
        var msg = new ClientRequestMessage(methodName, ...args).WithAuthorization(this._accessTokenFactory);
        return this._hubConnection.invoke("InvokeMessageResult", msg).catch(err => Promise.reject(this.extractException(err)));
    }
    /** Invokes a hub method on the server using the specified name and arguments. Does not wait for a response from the receiver.
     *
     * The Promise returned by this method resolves when the client has sent the invocation to the server. The server may still
     * be processing the invocation.
     *
     * @param {string} methodName The name of the server method to invoke.
     * @param {any[]} args The arguments used to invoke the server method.
     * @returns {Promise<void>} A Promise that resolves when the invocation has been successfully sent, or rejects with an error.
     */
    send(methodName, ...args) {
        var msg = new ClientRequestMessage(methodName, ...args).WithAuthorization(this._accessTokenFactory);
        return this._hubConnection.send("SendMessage", msg).catch(err => Promise.reject(this.extractException(err)));
    }
    /** Invokes a streaming hub method on the server using the specified name and arguments.
     *
     * @typeparam T The type of the items returned by the server.
     * @param {string} methodName The name of the server method to invoke.
     * @param {any[]} args The arguments used to invoke the server method.
     * @returns {IStreamResult<T>} An object that yields results from the server as they are received.
     */
    stream(methodName, ...args) {
        var msg = new ClientRequestMessage(methodName, ...args).WithAuthorization(this._accessTokenFactory);
        return this._hubConnection.stream("StreamMessage", msg);
    }
    /** Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param {string} methodName The name of the hub method to define.
     * @param {Function} newMethod The handler that will be raised when the hub method is invoked.
     */
    on(methodName, newMethod) {
        this._hubConnection.on(methodName, newMethod);
    }
    onServerRequest(methodName, func) {
        this._serverRequestHandlers.set(methodName, func);
        return this;
    }
    off(methodName, method) {
        if (!method) {
            this._hubConnection.off(methodName);
        }
        else {
            this._hubConnection.off(methodName, method);
        }
    }
    asSignalRHubConnection() {
        return this._hubConnection;
    }
    static create(hubConnection, options) {
        if (hubConnection instanceof Function) {
            var hubConnectionBuilder = new signalR.HubConnectionBuilder();
            hubConnection(hubConnectionBuilder);
            return new HARRRConnection(hubConnectionBuilder.build(), options);
        }
        return new HARRRConnection(hubConnection, options);
    }
    extractException(error) {
        const regex = /.*\[(.*)\]\s*(.*)/mg;
        var matches = regex.exec(error.message);
        return {
            type: (matches === null || matches === void 0 ? void 0 : matches[1]) || "Error",
            message: (matches === null || matches === void 0 ? void 0 : matches[2]) || error.message
        };
    }
}
