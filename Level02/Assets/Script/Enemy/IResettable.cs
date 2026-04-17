public interface IResettable
{
    string ResettableID { get; }
    void SaveInitialState();
    void ResetState();
}