namespace TesouroDireto.Application.Common.Interfaces;

public interface ICacheInvalidator
{
    void InvalidateTitulos();
    void InvalidatePrecos();
    void InvalidateTributos();
    void InvalidateFeriados();
}
