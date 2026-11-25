using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ToeicSpace.Notification.Infrastructure.Persistence;

internal static class PersistenceExceptionTranslator
{
    public static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is MySqlException { Number: 1062 };
}
