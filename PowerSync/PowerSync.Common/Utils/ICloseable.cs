namespace PowerSync.Common.Utils;

public interface ICloseable
{
    public void Close();
}

public interface ICloseableAsync
{
    public Task Close();
}

