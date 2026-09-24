
public interface IRigExtension
{
    public RigExtensionUpdateMethod updateMethod { get; set; }

    public void Initialize(LocalRig localRig);
    public abstract void Execute();
}
