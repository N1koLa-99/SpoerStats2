using SpoerStats2.Models;
using SpoerStats2.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class ResultService
{
    private readonly IResultRepository _resultRepository;
    private readonly IUserRepository _userRepository;

    public ResultService(IResultRepository resultRepository, IUserRepository userRepository)
    {
        _resultRepository = resultRepository;
        _userRepository = userRepository;
    }

    private static bool IsCoachOrAdmin(int roleId) => roleId == 2 || roleId == 3;

    public async Task<IEnumerable<Result>> GetAllResults() => await _resultRepository.GetAllResults();

    public async Task<IEnumerable<Result>> GetResultsForUser(int requesterId, int roleId, int clubId)
    {
        if (IsCoachOrAdmin(roleId))
            return await GetResultsForClub(clubId);

        return await _resultRepository.GetResultsByUserId(requesterId);
    }

    public async Task<Result> GetResultById(int id, int requesterId, int roleId, int clubId)
    {
        var result = await _resultRepository.GetResultById(id)
                     ?? throw new KeyNotFoundException("Резултатът не е намерен.");

        if (IsCoachOrAdmin(roleId))
        {
            var targetUser = await _userRepository.GetUserById(result.UserId);
            if (targetUser == null || targetUser.ClubID != clubId)
                throw new UnauthorizedAccessException("Нямате право да видите този резултат.");
        }
        else if (result.UserId != requesterId)
        {
            throw new UnauthorizedAccessException("Нямате право да видите този резултат.");
        }

        return result;
    }

    public async Task<IEnumerable<Result>> GetResultsForClub(int clubId)
    {
        var clubUsers = await _userRepository.GetUsersByClubId(clubId);
        return await _resultRepository.GetResultsByUserIds(clubUsers.Select(u => u.Id));
    }

    public async Task AddResultsBulk(IEnumerable<Result> results, int requesterId, int roleId, int clubId)
    {
        if (!IsCoachOrAdmin(roleId))
            throw new UnauthorizedAccessException("Нямате права за масово добавяне.");

        var clubUsers = await _userRepository.GetUsersByClubId(clubId);
        var allowedUserIds = clubUsers.Select(u => u.Id).ToHashSet();

        if (results.Any(r => !allowedUserIds.Contains(r.UserId)))
            throw new UnauthorizedAccessException("Един или повече атлети не принадлежат към вашия клуб.");

        await _resultRepository.AddResultsBulk(results);
    }

    public async Task AddResult(Result result, int requesterId, int roleId, int clubId)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (result.ValueTime <= 0) throw new ArgumentException("Невалидно време.");

        if (IsCoachOrAdmin(roleId))
        {
            var targetUser = await _userRepository.GetUserById(result.UserId);
            if (targetUser == null || targetUser.ClubID != clubId)
                throw new UnauthorizedAccessException("Нямате право за този потребител.");
        }
        else if (result.UserId != requesterId)
        {
            throw new UnauthorizedAccessException("Не можете да добавяте резултати на други атлети.");
        }

        await _resultRepository.AddResult(result);
    }

    public async Task UpdateResult(Result result, int requesterId, int roleId, int clubId)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        var existing = await _resultRepository.GetResultById(result.Id)
                      ?? throw new KeyNotFoundException("Не е намерен.");

        // ✅ Проверка за достъп по реалния owner от БД
        if (IsCoachOrAdmin(roleId))
        {
            var targetUser = await _userRepository.GetUserById(existing.UserId);
            if (targetUser?.ClubID != clubId)
                throw new UnauthorizedAccessException("Нямате достъп.");
        }
        else if (existing.UserId != requesterId)
        {
            throw new UnauthorizedAccessException("Нямате достъп.");
        }

        // ✅ Anti-mass-assignment: забраняваме смяна на owner (UserId)
        if (result.UserId != 0 && result.UserId != existing.UserId)
            throw new UnauthorizedAccessException("Не можете да променяте атлета на резултата.");

        // Форсираме оригиналния owner
        result.UserId = existing.UserId;

        await _resultRepository.UpdateResult(result);
    }

    public async Task DeleteResult(int id, int requesterId, int roleId, int clubId)
    {
        // ✅ auth check преди delete
        var existing = await _resultRepository.GetResultById(id)
                      ?? throw new KeyNotFoundException("Не е намерен.");

        if (IsCoachOrAdmin(roleId))
        {
            var targetUser = await _userRepository.GetUserById(existing.UserId);
            if (targetUser == null || targetUser.ClubID != clubId)
                throw new UnauthorizedAccessException("Нямате достъп.");
        }
        else if (existing.UserId != requesterId)
        {
            throw new UnauthorizedAccessException("Нямате достъп.");
        }

        await _resultRepository.DeleteResult(id);
    }

    public async Task<IEnumerable<Result>> GetResultsByUserIdAndDisciplineId(int userId, int disciplineId)
    {
        var res = await _resultRepository.GetResultsByUserId(userId);
        return res.Where(r => r.DisciplineId == disciplineId);
    }

    public async Task<IEnumerable<ResultSplit>> GetSplitsForResult(int resultId, int requesterId, int roleId, int clubId)
    {
        // reuse проверките за достъп върху самия резултат
        _ = await GetResultById(resultId, requesterId, roleId, clubId);
        return await _resultRepository.GetSplitsByResultId(resultId);
    }

    // ✅ НОВО: raw results за coach таблицата (ВСИЧКИ резултати, не само best)
    public async Task<IEnumerable<Result>> GetResultsByClubAndDiscipline(
        int clubId,
        int disciplineId,
        int requesterId,
        int requesterRoleId,
        int requesterClubId)
    {
        if (clubId <= 0) throw new ArgumentException("Невалиден clubId.");
        if (disciplineId <= 0) throw new ArgumentException("Невалиден disciplineId.");

        // Admin (3) -> достъп до всички клубове
        // Coach (2) -> само до своя клуб
        // Athlete (1) -> няма достъп до този endpoint (по желание може да го разрешиш)
        if (requesterRoleId == 2 && requesterClubId != clubId)
            throw new UnauthorizedAccessException("Нямате достъп до резултатите на този клуб.");

        if (requesterRoleId != 2 && requesterRoleId != 3)
            throw new UnauthorizedAccessException("Нямате достъп до този ресурс.");

        var rows = await _resultRepository.GetResultsByClubAndDiscipline(clubId, disciplineId);

        return rows
            .OrderBy(r => r.ValueTime)
            .ThenByDescending(r => r.ResultDate);
    }

    public async Task<IEnumerable<ClubBestResultDto>> GetClubBestResultsByDiscipline(
        int clubId,
        int disciplineId,
        int requesterId,
        int requesterRoleId,
        int requesterClubId)
    {
        if (requesterRoleId == 2 && requesterClubId != clubId)
            throw new UnauthorizedAccessException("Нямате достъп до резултатите на този клуб.");

        if (requesterRoleId != 2 && requesterRoleId != 3)
            throw new UnauthorizedAccessException("Нямате достъп до този ресурс.");

        var rows = await _resultRepository.GetClubBestResultsByDiscipline(clubId, disciplineId);

        return rows
            .OrderBy(x => x.ValueTime)
            .ThenByDescending(x => x.ResultDate)
            .ThenBy(x => x.UserLastName)
            .ThenBy(x => x.UserFirstName);
    }
    public async Task<IEnumerable<ClubBestResultDto>> GetPlatformClubLeadersByDiscipline(
    int disciplineId,
    int requesterId,
    int requesterRoleId,
    int requesterClubId)
    {
        if (disciplineId <= 0)
            throw new ArgumentException("Невалиден disciplineId.");

        // Позволяваме на всички логнати роли
        if (requesterRoleId != 1 && requesterRoleId != 2 && requesterRoleId != 3)
            throw new UnauthorizedAccessException("Нямате достъп до този ресурс.");

        var rows = await _resultRepository.GetPlatformClubLeadersByDiscipline(disciplineId);

        // За сигурност сортираме пак и в service
        return disciplineId == 18
            ? rows.OrderByDescending(x => x.ValueTime)
                  .ThenByDescending(x => x.ResultDate)
                  .ThenBy(x => x.ClubName)
            : rows.OrderBy(x => x.ValueTime)
                  .ThenByDescending(x => x.ResultDate)
                  .ThenBy(x => x.ClubName);
    }
}