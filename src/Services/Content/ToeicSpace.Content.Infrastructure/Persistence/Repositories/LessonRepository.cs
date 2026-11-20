using Microsoft.EntityFrameworkCore;
using ToeicSpace.Content.Application.Interfaces.Repositories;
using ToeicSpace.Content.Domain.Entities;

namespace ToeicSpace.Content.Infrastructure.Persistence.Repositories;

public sealed class LessonRepository(ContentDbContext db) : ILessonRepository
{
    public Task<bool> IsObjectKeyUsedAsync(string key, CancellationToken ct) =>
        PersistenceExceptionTranslator.ExecuteAsync(async () =>
            await db.LessonVideos.AnyAsync(x => x.ObjectKey == key, ct) ||
            await db.LessonResources.AnyAsync(x => x.ObjectKey == key, ct));
}
