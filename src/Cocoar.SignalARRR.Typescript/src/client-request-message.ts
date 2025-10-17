export class ClientRequestMessage {

    Method: string;
    Arguments: Array<any>;
    Authorization?: string;

    constructor(methodName: string, ...args: any[] ) {
        this.Method = methodName;
        this.Arguments = args;
    }

    WithAuthorization(authorization: string | (() => string)) {
        if(authorization instanceof Function){
            authorization = authorization()
        }
        this.Authorization = authorization;
        return this;
    }

    

}
