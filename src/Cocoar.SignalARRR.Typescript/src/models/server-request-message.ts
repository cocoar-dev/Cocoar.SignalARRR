export interface ServerRequestMessage {
  Id: string;
  Method: string;
  Arguments?: unknown[];
  GenericArguments?: string[];
  CancellationGuid?: string;
  StreamId?: string;
}
