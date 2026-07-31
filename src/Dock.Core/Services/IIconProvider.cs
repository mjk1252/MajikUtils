namespace Dock.Core.Services;

public interface IIconProvider
{
    byte[]? GetIconPng(string path, int size);
}
