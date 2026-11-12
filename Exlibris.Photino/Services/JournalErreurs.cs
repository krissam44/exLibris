using Microsoft.Extensions.Logging;

namespace Exlibris.Photino.Services;

/// <summary>
/// Les erreurs, écrites dans <c>erreurs.log</c> à côté de la base.
///
/// <b>Sans lui, une exception de rendu est INVISIBLE</b> (constaté le
/// 12/11/2026) : Blazor arrête l'interface, plus un bouton ne répond, et rien
/// ne dit pourquoi — l'application n'a pas de console, et la journalisation
/// n'avait aucune destination. Le fichier se lit, se joint à un message, et ne
/// garde que les erreurs : il ne grossit que quand quelque chose ne va pas.
/// </summary>
public sealed class JournalErreurs(string fichier) : ILoggerProvider
{
    private readonly Lock _verrou = new();

    public ILogger CreateLogger(string categorie) => new Journal(this, categorie);

    public void Dispose() { }

    private void Ecrire(string texte)
    {
        lock (_verrou)
        {
            try { File.AppendAllText(fichier, texte); }
            catch (IOException) { /* un journal qui échoue ne doit rien casser de plus */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class Journal(JournalErreurs proprietaire, string categorie) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel niveau) => niveau >= LogLevel.Error;

        public void Log<TState>(LogLevel niveau, EventId id, TState etat, Exception? exception,
            Func<TState, Exception?, string> formater)
        {
            if (!IsEnabled(niveau)) return;
            proprietaire.Ecrire(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{niveau}] {categorie}{Environment.NewLine}"
                + $"{formater(etat, exception)}{Environment.NewLine}"
                + (exception is null ? string.Empty : exception + Environment.NewLine)
                + Environment.NewLine);
        }
    }
}
