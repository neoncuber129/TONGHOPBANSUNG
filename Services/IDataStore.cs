using Tonghopbansung.Models;

namespace Tonghopbansung.Services;

public interface IDataStore
{
    string DataDirectory { get; }
    string DataFilePath { get; }
    AppState Load();
    void Save(AppState state);
}
