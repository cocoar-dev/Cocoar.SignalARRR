namespace Cocoar.SignalARRR.SourceGenerator.Model;

internal enum ReturnTypeCategory {
    Void,
    Task,
    TaskOfT,
    SyncReturn,
    Observable,
    ChannelReader,
    AsyncEnumerable
}
