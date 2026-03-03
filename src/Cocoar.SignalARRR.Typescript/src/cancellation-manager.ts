export class CancellationManager {
  private _controllers = new Map<string, AbortController>();

  create(id: string): AbortSignal {
    const controller = new AbortController();
    this._controllers.set(id, controller);
    return controller.signal;
  }

  cancel(id: string): void {
    const controller = this._controllers.get(id);
    if (controller) {
      controller.abort();
      this._controllers.delete(id);
    }
  }

  remove(id: string): void {
    this._controllers.delete(id);
  }
}
