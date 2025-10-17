export declare class ClientRequestMessage {
    Method: string;
    Arguments: Array<any>;
    Authorization?: string;
    constructor(methodName: string, ...args: any[]);
    WithAuthorization(authorization: string | (() => string)): this;
}
