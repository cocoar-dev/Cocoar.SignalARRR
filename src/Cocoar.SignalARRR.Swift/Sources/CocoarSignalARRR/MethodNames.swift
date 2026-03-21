import Foundation

/// String constants matching `Cocoar.SignalARRR.Common.Constants.MethodNames`.
///
/// These are the SignalR hub method names used by the SignalARRR wire protocol.
public enum MethodNames {
    // MARK: - Client → Server

    public static let invokeMessageOnServer = "InvokeMessage"
    public static let invokeMessageResultOnServer = "InvokeMessageResult"
    public static let sendMessageToServer = "SendMessage"
    public static let streamMessageFromServer = "StreamMessage"

    // MARK: - Server → Client

    public static let invokeServerRequest = "InvokeServerRequest"
    public static let replyServerRequest = "ReplyServerRequest"
    public static let challengeAuthentication = "ChallengeAuthentication"
    public static let invokeServerMessage = "InvokeServerMessage"
    public static let cancelTokenFromServer = "CancelTokenFromServer"

    // MARK: - Streaming

    public static let streamItemToServer = "StreamItemToServer"
    public static let streamCompleteToServer = "StreamCompleteToServer"
}
