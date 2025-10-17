export class ClientResponseMessage {

    Id: string;

    Payload?: any;

    ErrorMessage?: string;

    constructor(id: string) {
        this.Id = id;
    }
}