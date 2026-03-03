export interface ClientRequestMessage {
  Method: string;
  Arguments: unknown[];
  Authorization?: string;
  GenericArguments?: string[];
}
