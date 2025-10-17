import * as signalR from "@microsoft/signalr";
import { ClientRequestMessage } from './client-request-message';
import { ClientResponseMessage } from "./client-response-message";
import { ServerRequestMessage } from "./server-request-message";
import { HARRRConnectionOptions } from "./harrr-connection-options";

export class HARRRConnection {

    private _hubConnection: signalR.HubConnection;
    private _options: HARRRConnectionOptions;
    private _accessTokenFactory: () => string = () => "";

    private _serverRequestHandlers: Map<string, (...args: any[]) => any> = new Map<string, (...args: any[]) => any>();

    /** Indicates the url of the {@link HubConnection} to the server. */
    public get baseUrl() {
        return this._hubConnection.baseUrl;
    }

    /**
     * Sets a new url for the HubConnection. Note that the url can only be changed when the connection is in either the Disconnected or
     * Reconnecting states.
     * @param {string} url The url to connect to.
     */
    public set baseUrl(value: string) {
        this._hubConnection.baseUrl = value;
    }


    /** Represents the connection id of the {@link HubConnection} on the server. The connection id will be null when the connection is either
     *  in the disconnected state or if the negotiation step was skipped.
     */
    public get connectionId() {
        return this._hubConnection.connectionId;
    }


    /** Indicates the state of the {@link HubConnection} to the server. */
    public get state() {
        return this._hubConnection.state;
    }


    /** The server timeout in milliseconds.
     *
     * If this timeout elapses without receiving any messages from the server, the connection will be terminated with an error.
     * The default timeout value is 30,000 milliseconds (30 seconds).
     */
    public get serverTimeoutInMilliseconds() {
        return this._hubConnection.serverTimeoutInMilliseconds;
    }

    public set serverTimeoutInMilliseconds(value: number) {
        this._hubConnection.serverTimeoutInMilliseconds = value;
    }


    /** Default interval at which to ping the server.
     *
     * The default value is 15,000 milliseconds (15 seconds).
     * Allows the server to detect hard disconnects (like when a client unplugs their computer).
     */
    public get keepAliveIntervalInMilliseconds() {
        return this._hubConnection.keepAliveIntervalInMilliseconds;
    }

    public set keepAliveIntervalInMilliseconds(value: number) {
        this._hubConnection.keepAliveIntervalInMilliseconds = value;
    }


    constructor(hubConnection: signalR.HubConnection, options?: HARRRConnectionOptions) {
        this._hubConnection = hubConnection;
        this._options = options ?? new HARRRConnectionOptions();
        this._accessTokenFactory = (<any>this._hubConnection).connection?._options?.accessTokenFactory ?? (<any>this._hubConnection).connection?._accessTokenFactory;

        this._hubConnection.on("ChallengeAuthentication", (request: ServerRequestMessage) => {

            const msg = new ClientResponseMessage(request.Id);
            msg.Payload = this._accessTokenFactory();
            this._hubConnection.send("ReplyServerRequest", msg);
        })

        this._hubConnection.on("InvokeServerRequest", (request: ServerRequestMessage) => {

            const msg = new ClientResponseMessage(request.Id);
           
            const handler = this._serverRequestHandlers.get(request.Method);
            if(handler) {
                
                var result;
                if(request.Arguments) {
                    result = handler(request.Arguments)
                } else {
                    result = handler()
                }

                msg.Payload = result;
                
                if(this._options.HttpResponse) {
                    const payload = JSON.stringify(msg)
                    var url = ""
                    var _headers = {
                        'Content-Type': 'application/json; charset=UTF-8',
                        'Authorization': this._accessTokenFactory()
                    }
                    fetch(url, {
                        method: 'POST',
                        body: payload,
                        headers: _headers
                    })
                } else {
                    this._hubConnection.send("ReplyServerRequest", msg);
                }
                
            }

        })
    }


    /** Starts the connection.
     *
     * @returns {Promise<void>} A Promise that resolves when the connection has been successfully established, or rejects with an error.
     */
    public start() {
        return this._hubConnection.start();
    }


    /** Stops the connection.
     *
     * @returns {Promise<void>} A Promise that resolves when the connection has been successfully terminated, or rejects with an error.
     */
    public stop() {
        return this._hubConnection.stop();
    }


    /** Registers a handler that will be invoked when the connection is closed.
     *
     * @param {Function} callback The handler that will be invoked when the connection is closed. Optionally receives a single argument containing the error that caused the connection to close (if any).
     */
    public onClose(callback: (error?: Error) => void) {
        this._hubConnection.onclose(callback);
    }


    /** Registers a handler that will be invoked when the connection starts reconnecting.
     *
     * @param {Function} callback The handler that will be invoked when the connection starts reconnecting. Optionally receives a single argument containing the error that caused the connection to start reconnecting (if any).
     */
    public onReconnecting(callback: (error?: Error) => void) {
        this._hubConnection.onreconnecting(callback);
    }


    /** Registers a handler that will be invoked when the connection successfully reconnects.
     *
     * @param {Function} callback The handler that will be invoked when the connection successfully reconnects.
     */
    public onReconnected(callback: (connectionId?: string) => void) {
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
    public invoke<T>(methodName: string, ...args: any[]) {
        var msg = new ClientRequestMessage(methodName, ...args).WithAuthorization(this._accessTokenFactory);
        return this._hubConnection.invoke<T>("InvokeMessageResult", msg).catch(err => Promise.reject(this.extractException(err)));
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
    public send(methodName: string, ...args: any[]) {
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
    public stream<T>(methodName: string, ...args: any[]) {
        var msg = new ClientRequestMessage(methodName, ...args).WithAuthorization(this._accessTokenFactory);
        return this._hubConnection.stream<T>("StreamMessage", msg);
    }


    /** Registers a handler that will be invoked when the hub method with the specified method name is invoked.
     *
     * @param {string} methodName The name of the hub method to define.
     * @param {Function} newMethod The handler that will be raised when the hub method is invoked.
     */
    public on(methodName: string, newMethod: (...args: any[]) => void) {
        this._hubConnection.on(methodName, newMethod);
    }

    public onServerRequest(methodName: string, func: (...args: any[]) => any) {
        this._serverRequestHandlers.set(methodName, func);
        return this;
    }
    

    /** Removes all handlers for the specified hub method.
     *
     * @param {string} methodName The name of the method to remove handlers for.
     */
    public off(methodName: string): void;

    /** Removes the specified handler for the specified hub method.
     *
     * You must pass the exact same Function instance as was previously passed to {@link @microsoft/signalr.HubConnection.on}. Passing a different instance (even if the function
     * body is the same) will not remove the handler.
     *
     * @param {string} methodName The name of the method to remove handlers for.
     * @param {Function} method The handler to remove. This must be the same Function instance as the one passed to {@link @microsoft/signalr.HubConnection.on}.
     */
    public off(methodName: string, method: (...args: any[]) => void): void;
    public off(methodName: string, method?: (...args: any[]) => void) {
        if (!method) {
            this._hubConnection.off(methodName);
        } else {
            this._hubConnection.off(methodName, method);
        }
    }


    public asSignalRHubConnection() {
        return this._hubConnection;
    }

    public static create(hubConnection: signalR.HubConnection | ((builder: signalR.HubConnectionBuilder) => void), options?: HARRRConnectionOptions) {

        if (hubConnection instanceof Function) {
            var hubConnectionBuilder = new signalR.HubConnectionBuilder();
            hubConnection(hubConnectionBuilder);
            return new HARRRConnection(hubConnectionBuilder.build(), options);
        }

        return new HARRRConnection(hubConnection, options);
    }

    private extractException(error: any) {
        const regex = /.*\[(.*)\]\s*(.*)/mg;
        var matches = regex.exec(error.message);
        return {
            type: matches?.[1] || "Error",
            message: matches?.[2] || error.message
        }
    }
}
