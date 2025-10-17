export class ClientRequestMessage {
    constructor(methodName, ...args) {
        this.Method = methodName;
        this.Arguments = args;
    }
    WithAuthorization(authorization) {
        if (authorization instanceof Function) {
            authorization = authorization();
        }
        this.Authorization = authorization;
        return this;
    }
}
