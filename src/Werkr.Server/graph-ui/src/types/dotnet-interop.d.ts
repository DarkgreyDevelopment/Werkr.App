/** Callback interface for .NET interop via DotNetObjectReference. */
export interface DotNetObjectReference {
  invokeMethodAsync( methodName: string, ...args: unknown[] ): Promise<void>;
  dispose(): void;
}
