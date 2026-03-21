namespace TesouroDireto.Domain.DiasUteis;

public sealed class DiasUteisCalculator
{
    public int Calcular(DateOnly inicio, DateOnly fim, IReadOnlyCollection<DateOnly> feriados)
    {
        if (inicio >= fim)
        {
            return 0;
        }

        var feriadoSet = feriados.Count > 0
            ? new HashSet<DateOnly>(feriados)
            : new HashSet<DateOnly>();

        var count = 0;
        var current = inicio.AddDays(1);

        while (current <= fim)
        {
            var dayOfWeek = current.DayOfWeek;

            if (dayOfWeek != DayOfWeek.Saturday
                && dayOfWeek != DayOfWeek.Sunday
                && !feriadoSet.Contains(current))
            {
                count++;
            }

            current = current.AddDays(1);
        }

        return count;
    }
}
