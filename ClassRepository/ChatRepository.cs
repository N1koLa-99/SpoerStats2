using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Collections.Generic;
using Dapper;
using SpoerStats2.IRepository;
using SpoerStats2.Models;

namespace SpoerStats2.ClassRepository
{
    public class ChatRepository : IChatRepository
    {
        private readonly string _cs;
        public ChatRepository(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        // -------- Пользователи --------
        public async Task<(int Id, int RoleID, int ClubID)> GetUserBriefAsync(int userId)
        {
            const string sql = @"SELECT Id, RoleID, ClubID FROM dbo.Users WHERE Id=@Id";
            using var cn = new SqlConnection(_cs);
            var row = await cn.QuerySingleAsync(sql, new { Id = userId });
            return ((int)row.Id, (int)row.RoleID, (int)row.ClubID);
        }

        // -------- Чат създаване --------
        public async Task<int> CreateChatAsync(int clubId, string name, int creatorId, IEnumerable<int> memberIds)
        {
            using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            var chatId = await cn.ExecuteScalarAsync<int>(
                new CommandDefinition(@"
INSERT INTO dbo.Chats (ClubId, Name, IsGroup, CreatedByUserId, CreatedAt, LastActivityAt)
VALUES (@ClubId, @Name, 1, @Creator, SYSUTCDATETIME(), SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new { ClubId = clubId, Name = name, Creator = creatorId },
                    transaction: tx, commandTimeout: 30));

            // създател = админ
            await cn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO dbo.ChatMembers (ChatId, UserId, IsAdmin, JoinedAt) VALUES (@C,@U,1,SYSUTCDATETIME());",
                    new { C = chatId, U = creatorId }, transaction: tx, commandTimeout: 30));

            // допустими членове
            var valid = (await cn.QueryAsync<int>(
                new CommandDefinition(@"
SELECT Id
FROM dbo.Users
WHERE Id IN @Ids AND ClubID=@ClubId
      AND ISNULL(IsActive,1)=1
      AND ISNULL(StatusID,0) NOT IN (1,3)
      AND DeletedAt IS NULL",
                    new
                    {
                        Ids = memberIds.Distinct().Where(x => x != creatorId).ToArray(),
                        ClubId = clubId
                    },
                    transaction: tx, commandTimeout: 30))).ToList();

            if (valid.Count > 0)
            {
                var values = string.Join(",", valid.Select((_, i) => $"(@Id{i})"));
                var p = new DynamicParameters(new { ChatId = chatId });
                for (var i = 0; i < valid.Count; i++) p.Add($"Id{i}", valid[i]);

                await cn.ExecuteAsync(
                    new CommandDefinition(
                        $"INSERT INTO dbo.ChatMembers (ChatId, UserId, IsAdmin, JoinedAt) SELECT @ChatId, v.Id, 0, SYSUTCDATETIME() FROM (VALUES {values}) v(Id);",
                        p, transaction: tx, commandTimeout: 30));

                await cn.ExecuteAsync(
                    new CommandDefinition(
                        $"MERGE dbo.ChatReads AS t USING (SELECT @ChatId ChatId, v.Id UserId FROM (VALUES {values}) v(Id)) s " +
                        "ON (t.ChatId=s.ChatId AND t.UserId=s.UserId) " +
                        "WHEN NOT MATCHED THEN INSERT (ChatId, UserId, LastReadAt) VALUES (s.ChatId, s.UserId, SYSUTCDATETIME());",
                        p, transaction: tx, commandTimeout: 30));
            }

            tx.Commit();
            return chatId;
        }

        // -------- Членства / Роли --------
        // устойчива проверка с retry при timeout (-2) и по-дълъг таймаут
        public async Task<bool> IsMemberAsync(int chatId, int userId)
        {
            using var cn = new SqlConnection(_cs);
            const string sql = @"
SELECT TOP (1) 1
FROM dbo.ChatMembers
WHERE ChatId=@C AND UserId=@U;";

            async Task<bool> TryOnce(int seconds)
            {
                var v = await cn.ExecuteScalarAsync<int?>(
                    new CommandDefinition(sql, new { C = chatId, U = userId }, commandTimeout: seconds));
                return v.HasValue;
            }

            try { return await TryOnce(10); }
            catch (SqlException ex) when (ex.Number == -2) // timeout
            { return await TryOnce(30); }
        }

        public async Task<bool> IsAdminAsync(int chatId, int userId)
        {
            using var cn = new SqlConnection(_cs);
            const string sql = @"
SELECT TOP (1) 1
FROM dbo.ChatMembers
WHERE ChatId=@C AND UserId=@U AND IsAdmin=1;";
            var v = await cn.ExecuteScalarAsync<int?>(
                new CommandDefinition(sql, new { C = chatId, U = userId }, commandTimeout: 20));
            return v.HasValue;
        }

        // -------- Съобщения --------
        public async Task<long> AddMessageAsync(ChatMessage message)
        {
            using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            var msgId = await cn.ExecuteScalarAsync<long>(
                new CommandDefinition(@"
INSERT INTO dbo.ChatMessages (ChatId, SenderId, Content, CreatedAt)
VALUES (@ChatId, @SenderId, @Content, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                    message, transaction: tx, commandTimeout: 30));

            await cn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE dbo.Chats WITH (ROWLOCK) SET LastActivityAt = SYSUTCDATETIME() WHERE Id=@C",
                    new { C = message.ChatId }, transaction: tx, commandTimeout: 20));

            tx.Commit();
            return msgId;
        }

        // ВАЖНО: async + await, иначе връзката се затваря преди изпълнение
        public async Task AddAttachmentAsync(ChatMessageAttachment a)
        {
            using var cn = new SqlConnection(_cs);
            var def = new CommandDefinition(@"
INSERT INTO dbo.ChatMessageAttachments (ChatMessageId, FileUrl, FileType, CreatedAt)
VALUES (@ChatMessageId, @FileUrl, @FileType, SYSUTCDATETIME());",
                a, commandTimeout: 30);

            await cn.ExecuteAsync(def);
        }

        // -------- Моите чатове (/mine) --------
        public async Task<IEnumerable<object>> GetMyChatsAsync(int meUserId)
        {
            using var cn = new SqlConnection(_cs);

            const string sql = @"
WITH MyChats AS (
    SELECT 
        c.Id,
        c.Name,
        CAST(NULL AS nvarchar(400)) AS PhotoUrl,
        COALESCE(c.LastActivityAt, c.CreatedAt) AS LastActivityAt
    FROM dbo.Chats AS c
    INNER JOIN dbo.ChatMembers AS cm
        ON cm.ChatId = c.Id
       AND cm.UserId = @Me
)
SELECT
    mc.Id,
    mc.Name,
    mc.PhotoUrl,
    mc.LastActivityAt,
    lm.LastMessage,
    lm.LastMessageAt,
    ISNULL(cnt.TotalMessages, 0) AS MessageCount,
    ISNULL(ur.UnreadCount, 0)    AS UnreadCount
FROM MyChats mc
OUTER APPLY (
    SELECT TOP (1)
        m.Content   AS LastMessage,
        m.CreatedAt AS LastMessageAt
    FROM dbo.ChatMessages m
    WHERE m.ChatId = mc.Id
    ORDER BY m.Id DESC
) lm
OUTER APPLY (
    SELECT COUNT(1) AS TotalMessages
    FROM dbo.ChatMessages m
    WHERE m.ChatId = mc.Id
) cnt
LEFT JOIN dbo.ChatReads cr
  ON cr.ChatId = mc.Id
 AND cr.UserId = @Me
OUTER APPLY (
    SELECT COUNT(1) AS UnreadCount
    FROM dbo.ChatMessages m
    WHERE m.ChatId = mc.Id
      AND m.CreatedAt >
          ISNULL(cr.LastReadAt, CONVERT(datetime2,'1900-01-01',120))
) ur
ORDER BY COALESCE(lm.LastMessageAt, mc.LastActivityAt) DESC;";

            var def = new CommandDefinition(sql, new { Me = meUserId }, commandTimeout: 30);
            return await cn.QueryAsync(def);
        }

        // -------- Съобщения списък (/messages) --------
        public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(int chatId, long? afterId, int take)
        {
            using var cn = new SqlConnection(_cs);

            var defMsgs = new CommandDefinition(@"
SELECT TOP (@T)
       m.Id,
       m.ChatId,
       m.SenderId,
       m.Content,
       m.CreatedAt
FROM dbo.ChatMessages m
WHERE m.ChatId=@C AND (@A IS NULL OR m.Id > @A)
ORDER BY m.Id ASC;",
                new { C = chatId, A = afterId, T = take }, commandTimeout: 30);

            var msgs = (await cn.QueryAsync<ChatMessage>(defMsgs)).ToList();
            if (msgs.Count == 0) return msgs;

            var ids = msgs.Select(x => x.Id).ToArray();

            var defAtt = new CommandDefinition(@"
SELECT a.Id,
       a.ChatMessageId,
       a.FileUrl,
       a.FileType,
       a.CreatedAt
FROM dbo.ChatMessageAttachments a
WHERE a.ChatMessageId IN @Ids
ORDER BY a.Id ASC;",
                new { Ids = ids }, commandTimeout: 30);

            var atts = await cn.QueryAsync<ChatMessageAttachment>(defAtt);

            var lookup = atts.GroupBy(a => a.ChatMessageId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var m in msgs)
                if (lookup.TryGetValue(m.Id, out var list)) m.Attachments = list;

            return msgs;
        }

        public async Task MarkReadAsync(int chatId, int userId, DateTime atUtc)
        {
            using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();

            // 1) UPDATE
            var updated = await cn.ExecuteAsync(@"
UPDATE dbo.ChatReads
SET LastReadAt = @At
WHERE ChatId = @C AND UserId = @U;", new { C = chatId, U = userId, At = atUtc });

            if (updated == 0)
            {
                try
                {
                    // 2) INSERT ако няма ред
                    await cn.ExecuteAsync(@"
INSERT INTO dbo.ChatReads (ChatId, UserId, LastReadAt)
VALUES (@C, @U, @At);", new { C = chatId, U = userId, At = atUtc });
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    // race condition – редът е създаден междувременно → UPDATE отново
                    await cn.ExecuteAsync(@"
UPDATE dbo.ChatReads
SET LastReadAt = @At
WHERE ChatId = @C AND UserId = @U;", new { C = chatId, U = userId, At = atUtc });
                }
            }
        }

        // -------- Членове: списък --------
        public async Task<IEnumerable<ChatMemberDto>> GetMembersAsync(int chatId)
        {
            using var cn = new SqlConnection(_cs);

            const string sql = @"
SELECT
    cm.ChatId,
    cm.UserId,
    u.FirstName,
    u.LastName,
    cm.IsAdmin,
    cm.JoinedAt,
    cr.LastReadAt
FROM dbo.ChatMembers cm
JOIN dbo.Users u
    ON u.Id = cm.UserId
LEFT JOIN dbo.ChatReads cr
    ON cr.ChatId = cm.ChatId AND cr.UserId = cm.UserId
WHERE cm.ChatId = @ChatId
ORDER BY u.FirstName, u.LastName;";

            var def = new CommandDefinition(sql, new { ChatId = chatId }, commandTimeout: 30);
            return await cn.QueryAsync<ChatMemberDto>(def);
        }

        // -------- Членове (merge/leave) --------
        public async Task MergeMembersAsync(int chatId, int adminUserId, IEnumerable<int> addUserIds, IEnumerable<int> removeUserIds)
        {
            using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            var isAdmin = await cn.ExecuteScalarAsync<bool>(
                new CommandDefinition(@"
SELECT TOP (1) 1
FROM dbo.ChatMembers
WHERE ChatId=@C AND UserId=@U AND IsAdmin=1;",
                    new { C = chatId, U = adminUserId }, transaction: tx, commandTimeout: 20));
            if (!isAdmin) throw new UnauthorizedAccessException("Само админ на чата може да променя членове.");

            var clubId = await cn.ExecuteScalarAsync<int?>(
                new CommandDefinition("SELECT ClubId FROM dbo.Chats WHERE Id=@C",
                    new { C = chatId }, transaction: tx, commandTimeout: 20));
            if (clubId is null or 0) throw new InvalidOperationException("Чатът не съществува.");

            var adds = (addUserIds ?? Enumerable.Empty<int>()).Distinct().ToArray();
            var rems = (removeUserIds ?? Enumerable.Empty<int>()).Distinct().ToArray();

            if (adds.Length > 0)
            {
                var validAddIds = (await cn.QueryAsync<int>(
                    new CommandDefinition(@"
SELECT u.Id
FROM dbo.Users u
LEFT JOIN dbo.ChatMembers cm ON cm.ChatId=@C AND cm.UserId=u.Id
WHERE u.Id IN @Ids
  AND cm.UserId IS NULL
  AND u.ClubID=@ClubId
  AND ISNULL(u.IsActive,1)=1
  AND ISNULL(u.StatusID,0) NOT IN (1,3)
  AND u.DeletedAt IS NULL",
                        new { C = chatId, Ids = adds, ClubId = clubId.Value },
                        transaction: tx, commandTimeout: 30))).ToArray();

                if (validAddIds.Length > 0)
                {
                    var values = string.Join(",", validAddIds.Select((_, i) => $"(@Id{i})"));
                    var p = new DynamicParameters(new { ChatId = chatId });
                    for (var i = 0; i < validAddIds.Length; i++) p.Add($"Id{i}", validAddIds[i]);

                    await cn.ExecuteAsync(
                        new CommandDefinition(
                            $"INSERT INTO dbo.ChatMembers (ChatId, UserId, IsAdmin, JoinedAt) SELECT @ChatId, v.Id, 0, SYSUTCDATETIME() FROM (VALUES {values}) v(Id);",
                            p, transaction: tx, commandTimeout: 30));

                    await cn.ExecuteAsync(
                        new CommandDefinition(
                            $"MERGE dbo.ChatReads AS t USING (SELECT @ChatId ChatId, v.Id UserId FROM (VALUES {values}) v(Id)) s " +
                            "ON (t.ChatId=s.ChatId AND t.UserId=s.UserId) " +
                            "WHEN NOT MATCHED THEN INSERT (ChatId, UserId, LastReadAt) VALUES (s.ChatId, s.UserId, SYSUTCDATETIME());",
                            p, transaction: tx, commandTimeout: 30));
                }
            }

            if (rems.Length > 0)
            {
                var adminIds = (await cn.QueryAsync<int>(
                    new CommandDefinition(
                        "SELECT UserId FROM dbo.ChatMembers WHERE ChatId=@C AND IsAdmin=1",
                        new { C = chatId }, transaction: tx, commandTimeout: 20))).ToList();

                var cannotRemove = new HashSet<int>();
                if (adminIds.Count == 1) cannotRemove.Add(adminIds[0]); // единствен админ

                var safeRemovals = rems.Where(id => !cannotRemove.Contains(id)).ToArray();
                if (safeRemovals.Length > 0)
                {
                    await cn.ExecuteAsync(
                        new CommandDefinition(
                            "DELETE FROM dbo.ChatMembers WHERE ChatId=@C AND UserId IN @Ids",
                            new { C = chatId, Ids = safeRemovals }, transaction: tx, commandTimeout: 30));

                    await cn.ExecuteAsync(
                        new CommandDefinition(
                            "DELETE FROM dbo.ChatReads WHERE ChatId=@C AND UserId IN @Ids",
                            new { C = chatId, Ids = safeRemovals }, transaction: tx, commandTimeout: 30));
                }
            }

            tx.Commit();
        }

        public async Task LeaveAsync(int chatId, int userId)
        {
            using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            var admins = (await cn.QueryAsync<int>(
                new CommandDefinition(
                    "SELECT UserId FROM dbo.ChatMembers WHERE ChatId=@C AND IsAdmin=1",
                    new { C = chatId }, transaction: tx, commandTimeout: 20))).ToList();

            if (admins.Count == 1 && admins[0] == userId)
            {
                var membersCount = await cn.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(1) FROM dbo.ChatMembers WHERE ChatId=@C",
                        new { C = chatId }, transaction: tx, commandTimeout: 20));

                if (membersCount > 1)
                    throw new InvalidOperationException("Не можеш да напуснеш – ти си единственият админ. Прехвърли админ правата или добави друг админ.");
            }

            await cn.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM dbo.ChatMembers WHERE ChatId=@C AND UserId=@U",
                    new { C = chatId, U = userId }, transaction: tx, commandTimeout: 30));

            await cn.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM dbo.ChatReads WHERE ChatId=@C AND UserId=@U",
                    new { C = chatId, U = userId }, transaction: tx, commandTimeout: 30));

            tx.Commit();
        }

        // -------- Триене на съобщение --------
        public async Task<bool> DeleteMessageAsync(int chatId, long messageId, int requesterId)
        {
            using var cn = new SqlConnection(_cs);
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            // 1) Вземаме ChatId + SenderId за съобщението
            var info = await cn.QuerySingleOrDefaultAsync<(int ChatId, int SenderId)>(
                new CommandDefinition(@"
SELECT ChatId, SenderId
FROM dbo.ChatMessages
WHERE Id = @Id;",
                    new { Id = messageId }, transaction: tx, commandTimeout: 20));

            if (info.ChatId == 0)
            {
                tx.Rollback();
                return false; // няма такова съобщение
            }

            if (info.ChatId != chatId)
            {
                tx.Rollback();
                return false; // някой си играе с URL
            }

            // 2) Право за триене – или автор, или админ
            var isAdmin = await cn.ExecuteScalarAsync<int?>(
                new CommandDefinition(@"
SELECT TOP (1) 1
FROM dbo.ChatMembers
WHERE ChatId=@C AND UserId=@U AND IsAdmin=1;",
                    new { C = chatId, U = requesterId }, transaction: tx, commandTimeout: 20));

            var canDelete = info.SenderId == requesterId || isAdmin.HasValue;
            if (!canDelete)
            {
                tx.Rollback();
                return false;
            }

            // 3) Трием аттачите и самото съобщение
            await cn.ExecuteAsync(
                new CommandDefinition(
                    @"DELETE FROM dbo.ChatMessageAttachments WHERE ChatMessageId=@Id;",
                    new { Id = messageId }, transaction: tx, commandTimeout: 30));

            await cn.ExecuteAsync(
                new CommandDefinition(
                    @"DELETE FROM dbo.ChatMessages WHERE Id=@Id;",
                    new { Id = messageId }, transaction: tx, commandTimeout: 30));

            tx.Commit();
            return true;
        }
    }
}
