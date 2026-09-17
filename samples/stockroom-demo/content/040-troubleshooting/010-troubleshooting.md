---
id: troubleshooting
title: Maintenance & Troubleshooting
discipline: troubleshooting
audiences:
- support
- admin
---

# Maintenance & Troubleshooting

## Pre-release checklist {#pre-release-checklist}

Work through this before releasing the system to users after maintenance.

- [ ] Database backup verified
- [x] Disk space within range
  - [ ] Verify log rotation
- [ ] All scanner stations signed out and restarted

## Backup schedule {#backup-schedule}

| Component | Nr. | Activity | Target | Shift | Daily | Weekly | Monthly | Yearly |
|-----------|-----|----------|--------|-------|-------|--------|---------|--------|
| Database | 1 | Full backup | Backup share |  |  | x |  |  |
| Database | 2 | Transaction log | Backup share |  | x |  |  |  |
| Application | 3 | Configuration export | Backup share |  |  |  | x |  |
| Application | 4 | Certificate renewal | Certificate store |  |  |  |  | x |

## Routine tasks {#routine-tasks}

| Interval | Task | Reference |
|----------|------|-----------|
| Weekly | Review error log, clear finished tasks | — |
| Monthly | Reindex the database, check backup restore | Database guide, attachments |
| 6 months | Update scanner firmware, test label printer | Scanner manual, attachments |
| Yearly | Renew the server certificate | Certificate procedure |

### Database reindex {#database-reindex}

Run the reindex job outside working hours and record the duration in the maintenance log.
Investigate if the duration exceeds the value in the database guide.

### Certificate renewal {#certificate-renewal}

Renew the server certificate before it expires, restart the application service, and
archive the renewal confirmation with the handover.

## Common problems {#common-problems}

| Symptom | Likely cause | Action |
|---------|--------------|--------|
| Sign-in screen missing after startup | Station lost its network address | Restart the station |
| Repeated quantity mismatch on one shelf | Shelf label damaged or wrong | Reprint the label, recount |
| Dashboard shows stale numbers | Reporting job stopped | Restart the reporting service |

::: audience admin
Error codes map 1:1 to the application's event catalogue; see the API reference →
events for the authoritative list and originating modules.
:::
