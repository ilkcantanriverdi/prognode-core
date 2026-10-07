using Microsoft.Data.Sqlite;
using Prognode.Alarm;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Historian;
using Prognode.Contracts.Licensing;
using Prognode.Contracts.Tags;
using Prognode.Core.Batches;
using Prognode.Core.Tags;
using Prognode.Data.Sqlite;
using Prognode.Historian;
using Prognode.Licensing;
using Prognode.Notifications;
using Prognode.Trends;

internal static class ProcessDataContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync(string root)
    {
        var options = new SqliteDatabaseOptions(Path.Combine(root, "process-contract.db"));
        await new DatabaseInitializer(options).InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var device = new DeviceDefinition(Guid.NewGuid(), "Process PLC", "Modbus TCP",
            "Configured", "127.0.0.1", 502, 1, 1000, now, now);
        await new SqliteDeviceRepository(options).AddAsync(device);
        var tag = new TagDefinition(Guid.NewGuid(), device.Id, "Temperature", "40001",
            TagDataType.Float32, null, ModbusByteOrder.ABCD, "C", 1, 0, 1,
            true, now, now);
        var tags = new SqliteTagRepository(options);
        await tags.AddAsync(tag);

        var historianRepository = new SqliteHistorianRepository(options);
        var historian = new HistorianService(historianRepository,
            new SqliteHistorianConfigurationRepository(options), tags);
        foreach (var quality in Enum.GetValues<TagQuality>())
        {
            var snapshot = new TagValueSnapshot(tag.Id, device.Id, 12, 12, quality, now, "contract");
            var candidate = HistorianSamplePolicy.Create(snapshot, now, null);
            Check((candidate is not null) == (quality == TagQuality.Good),
                $"Historian must only record Good samples, not {quality}.");
        }
        Check(HistorianSamplePolicy.Create(null, now, null) is null &&
              HistorianSamplePolicy.Create(new TagValueSnapshot(tag.Id, device.Id, null, null,
                  TagQuality.Good, now, "contract"), now, null) is null,
            "Historian must skip absent and value-less snapshots.");
        var configuration = await historian.CreateConfigurationAsync(tag.Id, 10, 30);
        Check((await historian.GetRecordingTagIdsAsync()).Contains(tag.Id) &&
              configuration.Enabled, "Historian enrollment was not persisted.");

        var trends = new TrendService(new SqliteTrendRepository(options), tags);
        var trend = await trends.CreateAsync("Temperature", [tag.Id], ["#3ed7e8"], 60);
        Check((await trends.GetByIdAsync(trend.Id))?.TagIds.Single() == tag.Id,
            "Trend definition was not persisted with its Tag.");
        await trends.UpdateAsync(trend.Id, "Temperature history", [tag.Id],
            ["#ff7180"], 480);
        Check((await trends.GetByIdAsync(trend.Id)) is { Name: "Temperature history",
            DefaultWindowMinutes: 480 }, "Trend update was not persisted.");

        var qualities = Enum.GetValues<TagQuality>();
        var from = now.AddDays(-1);
        var firstMs = from.ToUnixTimeMilliseconds();
        var samples = new List<HistorianSample>(12_000);
        for (var i = 0; i < 12_000; i++)
        {
            var quality = qualities[i % qualities.Length];
            double? value = quality == TagQuality.Good ? 20 + (i % 27) * 0.1 : null;
            if (i == 3122) value = 365.75;
            if (i == 5131) value = -27.25;
            samples.Add(new HistorianSample(tag.Id, firstMs + i * 1000L,
                value, quality.ToString()));
        }
        await historianRepository.WriteBatchAsync(samples);
        var streamed = 0;
        long lastStreamedMs = long.MinValue;
        await foreach (var sample in historian.StreamRawAsync([tag.Id], from, from.AddSeconds(12_000)))
        {
            Check(sample.TimestampUnixMs > lastStreamedMs, "Historian archive stream is not ordered.");
            lastStreamedMs = sample.TimestampUnixMs;
            streamed++;
        }
        Check(streamed == 12_000 && lastStreamedMs == firstMs + 11_999_000,
            "Historian archive stream omitted raw records or crossed the exclusive boundary.");
        var series = await historian.QuerySeriesAsync(tag.Id, from,
            from.AddSeconds(12_000), 3000);
        var originals = samples.ToDictionary(s => s.TimestampUnixMs);
        Check(series.SampleCount == 12_000 && series.Points.Count <= 3000,
            "Historian long-range query exceeded its point budget.");
        Check(series.Minimum == -27.25 && series.Maximum == 365.75 &&
              series.Points.Any(p => p.Value == -27.25) &&
              series.Points.Any(p => p.Value == 365.75),
            "Historian decimation lost a real high/low excursion.");
        Check(qualities.All(q => series.Points.Any(p => p.Quality == q.ToString())) &&
              series.Points.All(p => originals.TryGetValue(p.Timestamp.ToUnixTimeMilliseconds(), out var original) &&
                  original.Value == p.Value && original.Quality == p.Quality),
            "Historian decimation invented samples or erased a quality state.");
        await historianRepository.DeleteBeforeAsync(tag.Id, from.AddSeconds(100));
        var retained = await historian.QuerySeriesAsync(tag.Id, from,
            from.AddSeconds(12_000), 3000);
        Check(retained.SampleCount == 11_900 &&
              retained.Points.All(p => p.Timestamp.ToUnixTimeMilliseconds() >= firstMs + 100_000),
            "Historian retention cleanup did not remove only expired samples.");
        Console.WriteLine("PASS historian enrollment/retention, trend CRUD and bounded 12,000-sample quality/spike query");

        var currentValues = new CurrentTagValueStore();
        var alarmEvents = new SqliteAlarmEventRepository(options);
        var runtime = new AlarmRuntimeStore(options.DatabasePath);
        var notifications = new NotificationEventStore(options.DatabasePath);
        var alarms = new AlarmService(new SqliteAlarmDefinitionRepository(options),
            alarmEvents, tags, runtime, notifications);
        var definition = await alarms.CreateAsync(tag.Id, "Temperature high", AlarmPriority.High,
            null, true, AlarmCondition.GreaterThanOrEqual, 80, 2, 0, 0,
            false, false, AlarmNotificationMode.NotifyOnce, 60, false);
        var engine = new AlarmEngine(currentValues, alarmEvents, runtime, notifications,
            new BatchService(new SqliteBatchRepository(options)));
        var tagMap = new Dictionary<Guid, TagDefinition> { [tag.Id] = tag };
        var deviceNames = new Dictionary<Guid, string> { [device.Id] = device.Name };
        currentValues.Set(Snapshot(tag, 90, TagQuality.Good));
        await engine.EvaluateAsync([definition], tagMap, deviceNames, CancellationToken.None);
        Check(alarms.GetActive().Count == 1, "Good high value did not activate alarm.");
        currentValues.Set(Snapshot(tag, null, TagQuality.Bad));
        await engine.EvaluateAsync([definition], tagMap, deviceNames, CancellationToken.None);
        Check(alarms.GetActive().Count == 1, "Bad data incorrectly cleared active alarm.");
        var restartedRuntime = new AlarmRuntimeStore(options.DatabasePath);
        var restartedEngine = new AlarmEngine(currentValues, alarmEvents, restartedRuntime,
            notifications, new BatchService(new SqliteBatchRepository(options)));
        var restartedAlarms = new AlarmService(new SqliteAlarmDefinitionRepository(options),
            alarmEvents, tags, restartedRuntime, notifications);
        Check(restartedAlarms.GetActive().Count == 1,
            "Active alarm occurrence was lost after runtime restart.");
        currentValues.Set(Snapshot(tag, 70, TagQuality.Good));
        await restartedEngine.EvaluateAsync([definition], tagMap, deviceNames, CancellationToken.None);
        var history = await restartedAlarms.GetHistoryAsync(10);
        Check(restartedAlarms.GetActive().Count == 0 && history.Count == 1 &&
              history[0].ClearedAt is not null,
            "Recovered Good value did not clear and persist alarm occurrence.");
        currentValues.Set(Snapshot(tag, 91, TagQuality.Good));
        await restartedEngine.EvaluateAsync([definition], tagMap, deviceNames, CancellationToken.None);
        currentValues.Set(Snapshot(tag, 69, TagQuality.Good));
        await restartedEngine.EvaluateAsync([definition], tagMap, deviceNames, CancellationToken.None);
        var repeatedHistory = (await restartedAlarms.GetHistoryAsync(10))
            .Where(x => x.AlarmKey == AlarmService.KeyFor(definition.Id)).ToArray();
        Check(repeatedHistory.Length == 2 &&
              repeatedHistory.Select(x => x.OccurrenceId).Distinct().Count() == 2 &&
              repeatedHistory.All(x => x.ClearedAt is not null),
            "Repeated alarm activation overwrote or merged the prior occurrence.");

        var repeatRule = await restartedAlarms.CreateAsync(tag.Id, "Repeat after ACK",
            AlarmPriority.High, null, true, AlarmCondition.GreaterThanOrEqual, 80, 0, 0, 0,
            true, false, AlarmNotificationMode.RepeatUntilAcknowledged, 15, false,
            cancellationToken: CancellationToken.None, requiresAcknowledgement: true);
        currentValues.Set(Snapshot(tag, 90, TagQuality.Good));
        await restartedEngine.EvaluateAsync([repeatRule], tagMap, deviceNames, CancellationToken.None);
        var beforeAck = restartedAlarms.GetActive().Single(x => x.DefinitionId == repeatRule.Id);
        Check(await restartedAlarms.AcknowledgeOccurrenceAsync(beforeAck.OccurrenceId) == OccurrenceAckResult.Acknowledged,
            "The repeat alarm could not be acknowledged.");
        var afterAck = restartedRuntime.Get(AlarmService.KeyFor(repeatRule.Id))!;
        restartedRuntime.SetActive(afterAck with { LastChangedAt = DateTimeOffset.UtcNow.AddSeconds(-20) });
        await using (var connection = new SqliteConnection(options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE notification_events SET created_at=$at WHERE occurrence_id=$id AND event_type='ACTIVE';";
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.AddSeconds(-20).ToString("O"));
            command.Parameters.AddWithValue("$id", beforeAck.OccurrenceId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
        var restartedRepeatEngine = new AlarmEngine(currentValues, alarmEvents, restartedRuntime, notifications,
            new BatchService(new SqliteBatchRepository(options)));
        await restartedRepeatEngine.EvaluateAsync([repeatRule], tagMap, deviceNames, CancellationToken.None);
        var reminder = notifications.GetRecent(30).FirstOrDefault(x =>
            x.OccurrenceId == beforeAck.OccurrenceId && x.EventType == "REMINDER");
        Check(reminder is not null && !reminder.RequiresAcknowledgement,
            "An acknowledged but still-active alarm did not repeat at its interval without requesting a duplicate ACK.");

        var noAckRule = await restartedAlarms.CreateAsync(tag.Id, "No ACK required",
            AlarmPriority.Medium, null, true, AlarmCondition.GreaterThanOrEqual, 80, 0, 0, 0,
            true, false, AlarmNotificationMode.NotifyOnce, 60, false,
            cancellationToken: CancellationToken.None, requiresAcknowledgement: false);
        await restartedRepeatEngine.EvaluateAsync([repeatRule, noAckRule], tagMap, deviceNames, CancellationToken.None);
        var noAckOccurrence = restartedAlarms.GetActive().Single(x => x.DefinitionId == noAckRule.Id);
        Check(noAckOccurrence.RequiresAcknowledgement == false &&
              (await restartedAlarms.AcknowledgeOccurrenceAsync(noAckOccurrence.OccurrenceId)) != OccurrenceAckResult.Acknowledged &&
              (await restartedAlarms.GetDefinitionsAsync()).Single(x => x.Id == noAckRule.Id).RequiresAcknowledgement == false,
            "A non-acknowledgeable alarm exposed an ACK or failed to persist its policy.");

        var ackToggleRule = await restartedAlarms.CreateAsync(tag.Id, "ACK policy update",
            AlarmPriority.Medium, null, true, AlarmCondition.GreaterThanOrEqual, 80, 0, 0, 0,
            true, false, AlarmNotificationMode.NotifyOnce, 60, false,
            cancellationToken: CancellationToken.None, requiresAcknowledgement: true);
        var ackToggleDefinition = (await restartedAlarms.GetDefinitionsAsync())
            .Single(x => x.Id == ackToggleRule.Id);
        await restartedRepeatEngine.EvaluateAsync([repeatRule, noAckRule, ackToggleDefinition],
            tagMap, deviceNames, CancellationToken.None);
        var beforeAckPolicyUpdate = restartedAlarms.GetActive()
            .Single(x => x.DefinitionId == ackToggleRule.Id);
        var updatedAckToggle = await restartedAlarms.UpdateAsync(ackToggleRule.Id,
            tag.Id, ackToggleRule.Text, ackToggleRule.Priority, ackToggleRule.BitIndex,
            ackToggleRule.TriggerValue, ackToggleRule.Condition, ackToggleRule.Threshold,
            ackToggleRule.Deadband, ackToggleRule.DelayOnMs, ackToggleRule.DelayOffMs,
            ackToggleRule.NotifyOnActive, ackToggleRule.NotifyOnCleared,
            ackToggleRule.NotificationMode, ackToggleRule.RepeatIntervalSeconds,
            ackToggleRule.ContinueAfterClearUntilAcknowledged,
            cancellationToken: CancellationToken.None, requiresAcknowledgement: false);
        var afterAckPolicyUpdate = restartedAlarms.GetActive()
            .Single(x => x.DefinitionId == ackToggleRule.Id);
        Check(afterAckPolicyUpdate.OccurrenceId == beforeAckPolicyUpdate.OccurrenceId &&
              afterAckPolicyUpdate.RequiresAcknowledgement == false &&
              await restartedAlarms.AcknowledgeOccurrenceAsync(beforeAckPolicyUpdate.OccurrenceId) != OccurrenceAckResult.Acknowledged &&
              (await restartedAlarms.GetDefinitionsAsync()).Single(x => x.Id == ackToggleRule.Id)
                  .RequiresAcknowledgement == false,
            "Disabling operator ACK did not persist immediately to the active occurrence and definition.");
        await restartedRepeatEngine.EvaluateAsync([repeatRule, noAckRule, updatedAckToggle],
            tagMap, deviceNames, CancellationToken.None);
        Check(restartedAlarms.GetActive().Single(x => x.DefinitionId == ackToggleRule.Id)
                  .OccurrenceId == beforeAckPolicyUpdate.OccurrenceId,
            "Updating ACK policy unnecessarily restarted the active alarm occurrence.");
        Console.WriteLine("PASS alarm ACK policy persistence, active occurrence updates, and interval reminders");

        Check(await restartedAlarms.DeleteAsync(definition.Id),
            "Configured alarm definition could not be deleted.");
        Check(!(await restartedAlarms.GetDefinitionsAsync()).Any(x=>x.Id==definition.Id),
            "Deleted alarm definition remained in SQLite.");
        Check((await restartedAlarms.GetHistoryAsync(10)).Any(x=>x.OccurrenceId==history[0].OccurrenceId),
            "Deleting a rule must preserve historical alarm occurrences.");
        var activeRule=await restartedAlarms.CreateAsync(tag.Id,"Remove while active",AlarmPriority.High,
            null,true,AlarmCondition.GreaterThanOrEqual,80,0,0,0,
            false,false,AlarmNotificationMode.NotifyOnce,60,false);
        currentValues.Set(Snapshot(tag,90,TagQuality.Good));
        await restartedEngine.EvaluateAsync([activeRule],tagMap,deviceNames,CancellationToken.None);
        Check(restartedAlarms.GetActive().Any(x=>x.DefinitionId==activeRule.Id),
            "Active delete test did not activate its alarm.");
        Check(await restartedAlarms.DeleteAsync(activeRule.Id) &&
            !restartedAlarms.GetActive().Any(x=>x.DefinitionId==activeRule.Id),
            "Deleting an active rule did not remove its runtime state.");
        Check((await restartedAlarms.GetHistoryAsync(10)).Any(x=>x.AlarmKey==AlarmService.KeyFor(activeRule.Id) && x.ClearedAt is not null),
            "Deleting an active rule left an open historical occurrence.");
        Check(await restartedAlarms.CountHistoryAsync() == 6,
            "Alarm history count must include persisted occurrences after rule deletion.");
        var template = (await alarmEvents.GetRecentAsync(1)).Single();
        for (var index = 0; index < 102; index++)
        {
            var occurrenceId = Guid.NewGuid();
            var activeAt = DateTimeOffset.UtcNow.AddMinutes(index + 1);
            await alarmEvents.AddAsync(template with { Id = 0, AlarmKey = "alarm:repeat-test",
                EventType = AlarmEventType.Active, Timestamp = activeAt, OccurrenceId = occurrenceId });
            await alarmEvents.AddAsync(template with { Id = 0, AlarmKey = "alarm:repeat-test",
                EventType = AlarmEventType.Cleared, Timestamp = activeAt.AddSeconds(5),
                OccurrenceId = occurrenceId });
        }
        var newestPage = await restartedAlarms.GetHistoryPageAsync(0, 100);
        var oldestPage = await restartedAlarms.GetHistoryPageAsync(100, 25);
        var filteredPage = await restartedAlarms.GetHistoryPageAsync(100, 25, "repeat-test");
        Check(await restartedAlarms.CountHistoryAsync() == 108 &&
              newestPage.Count == 100 && oldestPage.Count == 8 &&
              newestPage.Concat(oldestPage).Select(x => x.OccurrenceId).Distinct().Count() == 108 &&
              oldestPage.Any(x => x.OccurrenceId == history[0].OccurrenceId) &&
              await restartedAlarms.CountMatchingHistoryAsync("repeat-test", null) == 102 &&
              filteredPage.Count == 2,
            "Alarm history pagination lost or merged old repeated occurrences.");
        Console.WriteLine("PASS alarm active → Bad-quality hold → restart → clear and persisted occurrence");

        var entitlements = new LicenseEntitlements(CapacityLimit.Unlimited,
            CapacityLimit.Limited(100), CapacityLimit.Unlimited, CapacityLimit.Unlimited,
            true, false, false, new HashSet<string>(),
            new HashSet<string>(["ALARM", "HISTORIAN"]), false, false, 0);
        var licenseSnapshot = new LicenseSnapshot("LIC-PROCESS-CONTRACT", "Test", "ALARM_HISTORIAN",
            "ACTIVE", true, now.AddDays(7), now.AddDays(14), entitlements, "Test provider");
        var cloud = new CoreCloudLicenseStateStore(root);
        var license = new LicenseService(new FixedProvider(licenseSnapshot), cloud);
        Check(license.HasModule("ALARM") && license.HasModule("HISTORIAN") &&
              license.AllowsUniqueTagCount(100) && !license.AllowsUniqueTagCount(101),
            "Signed module or Tag-capacity policy failed.");
        cloud.Save(new CoreCloudLicenseStatus(true, "REVOKED", null, null, null, null,
            now, null, licenseSnapshot.LicenseId, true, now));
        Check(!license.HasModule("ALARM") && !license.HasModule("HISTORIAN") &&
              !license.AllowsUniqueTagCount(1),
            "Exact-license cloud revocation did not remove paid authority.");
        cloud.Save(new CoreCloudLicenseStatus(true, "REVOKED", null, null, null, null,
            now, null, "OTHER-LICENSE", true, now));
        Check(license.HasModule("ALARM") && license.HasModule("HISTORIAN"),
            "Revocation for another license disabled the local license.");
        var expiry = now.AddDays(30);
        var subscription = new LicenseSubscriptionV2("ALARM_HISTORIAN", "YEARLY",
            now, expiry, expiry.AddDays(7), 100, false, "WEB_V1_8");
        var payload = new LicensePayloadV2("test", "test", "test", "test", 1,
            "test", "test", subscription,
            new LicenseAccountV2("test", "test@example.invalid", "Test", "OWNER"),
            new OfflineAuthClaimV1(1, "test", 1, [], [], 1, 1, 1, 1, "test", 1),
            new LicenseEntitlementClaimsV2(true, true, "unlimited", null, null, null,
                100, false), now);
        var lifecycle = new LicenseEntitlementService();
        Check(lifecycle.GetLicenseStatus(payload, expiry.AddDays(-7).AddTicks(-1)) == "ACTIVE",
            "Lifecycle must remain ACTIVE one tick before EXPIRING_SOON.");
        Check(lifecycle.GetLicenseStatus(payload, expiry.AddDays(-7)) == "EXPIRING_SOON",
            "Lifecycle must become EXPIRING_SOON exactly seven days before expiry.");
        Check(lifecycle.GetLicenseStatus(payload, expiry) == "GRACE",
            "Lifecycle must become GRACE exactly at expiresAtUtc.");
        Check(lifecycle.GetLicenseStatus(payload, expiry.AddDays(7)) == "GRACE",
            "Lifecycle must remain GRACE exactly at graceUntilUtc.");
        Check(lifecycle.GetLicenseStatus(payload, expiry.AddDays(7).AddTicks(1)) == "EXPIRED",
            "Lifecycle must become EXPIRED one tick after graceUntilUtc.");
        Console.WriteLine("PASS license module/capacity, exact-ID revoke and commercial lifecycle boundaries");
    }

    private static TagValueSnapshot Snapshot(TagDefinition tag, double? value,
        TagQuality quality) => new(tag.Id, tag.DeviceId, value, value, quality,
            DateTimeOffset.UtcNow, "Process contract");

    private sealed class FixedProvider(LicenseSnapshot snapshot) : ILicenseProvider
    {
        public LicenseSnapshot GetCurrent() => snapshot;
    }
}
