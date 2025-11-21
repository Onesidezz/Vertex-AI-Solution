# Record Sync Scheduler - Deep Analysis

## Table of Contents
1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Core Components](#core-components)
4. [Quartz.NET Integration](#quartznet-integration)
5. [Job Implementation](#job-implementation)
6. [Scheduler Service](#scheduler-service)
7. [API Controller](#api-controller)
8. [Configuration](#configuration)
9. [Execution Flow](#execution-flow)
10. [Features and Capabilities](#features-and-capabilities)
11. [Security Considerations](#security-considerations)
12. [Monitoring and Observability](#monitoring-and-observability)
13. [Operational Guide](#operational-guide)
14. [Best Practices](#best-practices)
15. [Troubleshooting](#troubleshooting)

---

## Overview

The Record Sync Scheduler is a robust background job processing system built on top of **Quartz.NET** that automatically synchronizes Content Manager records and generates embeddings for semantic search capabilities. The system provides enterprise-grade features including configurable scheduling, job control, monitoring, and full REST API management.

### Purpose
- **Automated Data Synchronization**: Periodically fetch records from Content Manager
- **Embedding Generation**: Generate AI embeddings for semantic search
- **Scheduled Processing**: Run at configurable intervals using cron expressions
- **Manual Control**: Trigger jobs on-demand or pause/resume as needed
- **Monitoring**: Track job execution status, history, and performance

### Key Characteristics
- **Non-blocking**: Runs asynchronously without affecting API performance
- **Configurable**: Fully configurable via appsettings.json and REST API
- **Resilient**: Includes error handling, cancellation support, and job retry logic
- **Observable**: Comprehensive logging and status reporting
- **Controllable**: Full control via REST API endpoints

---

## Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                       ASP.NET Core Host                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌───────────────────────────────────────────────────────┐    │
│  │          RecordSyncSchedulerController                 │    │
│  │              (REST API Layer)                          │    │
│  └────────────────┬──────────────────────────────────────┘    │
│                   │                                             │
│                   ▼                                             │
│  ┌───────────────────────────────────────────────────────┐    │
│  │       RecordSyncSchedulerService                       │    │
│  │         (Business Logic Layer)                         │    │
│  └────────────────┬──────────────────────────────────────┘    │
│                   │                                             │
│                   ▼                                             │
│  ┌───────────────────────────────────────────────────────┐    │
│  │              Quartz.NET Scheduler                      │    │
│  │         (Job Scheduling Engine)                        │    │
│  └────────────────┬──────────────────────────────────────┘    │
│                   │                                             │
│                   ▼                                             │
│  ┌───────────────────────────────────────────────────────┐    │
│  │              RecordSyncJob                             │    │
│  │           (Job Implementation)                         │    │
│  └────────────────┬──────────────────────────────────────┘    │
│                   │                                             │
│                   ▼                                             │
│  ┌───────────────────────────────────────────────────────┐    │
│  │         IRecordEmbeddingService                        │    │
│  │      (Content Manager Integration)                     │    │
│  └─────────────────────────────────────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Technology Stack
- **Quartz.NET**: Enterprise-grade job scheduling library
- **ASP.NET Core**: Web framework and DI container
- **PostgreSQL + pgvector**: Vector storage for embeddings
- **Serilog**: Structured logging
- **Content Manager SDK**: Integration with TRIM/Content Manager

---

## Core Components

### 1. RecordSyncJob
**Location**: `DocumentProcessingAPI.Infrastructure\Jobs\RecordSyncJob.cs`

The actual job that gets executed on schedule. It orchestrates the entire sync process.

**Responsibilities**:
- Execute the scheduled sync operation
- Call IRecordEmbeddingService to process records
- Handle cancellation and errors gracefully
- Log comprehensive execution details
- Store execution results for monitoring

**Key Attributes**:
```csharp
[DisallowConcurrentExecution] // Prevents overlapping executions
```

### 2. RecordSyncSchedulerService
**Location**: `DocumentProcessingAPI.Infrastructure\Services\RecordSyncSchedulerService.cs`

Manages the lifecycle of the scheduler and provides control operations.

**Responsibilities**:
- Start/stop/pause/resume jobs
- Update cron schedules dynamically
- Query job status and execution history
- Modify job configuration at runtime
- Trigger manual job executions

### 3. RecordSyncSchedulerController
**Location**: `DocumentProcessingAPI.API\Controllers\RecordSyncSchedulerController.cs`

REST API endpoints for external control and monitoring.

**Responsibilities**:
- Expose HTTP endpoints for scheduler management
- Validate incoming requests
- Return structured JSON responses
- Provide cron expression examples and help

---

## Quartz.NET Integration

### Configuration (Currently Commented Out)

The scheduler is configured in `Program.cs` (lines 205-238) but is currently **commented out**. Here's the configuration:

```csharp
builder.Services.AddQuartz(q =>
{
    // Use Microsoft DI for job instantiation
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Configuration from appsettings.json
    var cronSchedule = builder.Configuration["RecordSync:CronSchedule"] ?? "0 0 * * * ?";
    var searchString = builder.Configuration["RecordSync:SearchString"] ?? "*";
    var enableSync = bool.Parse(builder.Configuration["RecordSync:Enabled"] ?? "true");

    // Job definition
    var jobKey = new JobKey("record-sync-job", "content-manager-sync");
    q.AddJob<RecordSyncJob>(opts => opts
        .WithIdentity(jobKey)
        .WithDescription("Syncs Content Manager records and generates embeddings")
        .UsingJobData("SearchString", searchString)
        .UsingJobData("EnableSync", enableSync)
        .StoreDurably());

    // Trigger with cron schedule
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("record-sync-trigger", "content-manager-sync")
        .WithCronSchedule(cronSchedule)
        .WithDescription($"Sync Content Manager records: {cronSchedule}"));
});

// Hosted service integration
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Scheduler management service
builder.Services.AddSingleton<RecordSyncSchedulerService>();
```

### Job and Trigger Identifiers

```csharp
Job Key:     "record-sync-job" in group "content-manager-sync"
Trigger Key: "record-sync-trigger" in group "content-manager-sync"
```

These identifiers are used throughout the system to reference the specific job.

### Dependency Injection Integration

Quartz.NET uses Microsoft's DI container via `UseMicrosoftDependencyInjectionJobFactory()`, allowing jobs to receive services through constructor injection:

```csharp
public RecordSyncJob(
    IRecordEmbeddingService recordEmbeddingService,
    ILogger<RecordSyncJob> logger)
```

---

## Job Implementation

### RecordSyncJob.Execute() Flow

```csharp
public async Task Execute(IJobExecutionContext context)
```

**Execution Steps**:

1. **Initialize Logging Context**
   ```csharp
   var jobId = context.FireInstanceId;
   var scheduledTime = context.ScheduledFireTimeUtc?.ToLocalTime();
   var actualTime = context.FireTimeUtc.ToLocalTime();
   ```

2. **Retrieve Job Configuration**
   ```csharp
   var dataMap = context.MergedJobDataMap;
   var searchString = dataMap.GetString("SearchString") ?? "*";
   var enableSync = dataMap.GetBooleanValue("EnableSync");
   ```

3. **Check Enable Flag**
   ```csharp
   if (!enableSync)
   {
       _logger.LogInformation("⏸️ Record sync is disabled. Skipping job execution.");
       return;
   }
   ```

4. **Process Records**
   ```csharp
   var processedCount = await _recordEmbeddingService.ProcessAllRecordsAsync(
       searchString,
       context.CancellationToken);
   ```

5. **Calculate Metrics**
   ```csharp
   var duration = DateTime.UtcNow - context.FireTimeUtc;
   ```

6. **Store Execution Result**
   ```csharp
   context.Result = new JobExecutionResult
   {
       Success = true,
       RecordsProcessed = processedCount,
       Duration = duration,
       CompletedAt = DateTime.UtcNow,
       Message = $"Successfully processed {processedCount} records"
   };
   ```

7. **Error Handling**
   - **OperationCanceledException**: Job was cancelled (manual stop)
   - **General Exception**: Unexpected error occurred
   - Both store failure information and create JobExecutionException

### Job Data Map

The JobDataMap stores configuration that persists across job executions:

| Key | Type | Purpose | Default |
|-----|------|---------|---------|
| SearchString | string | Content Manager search criteria | "*" |
| EnableSync | bool | Enable/disable sync without removing job | true |

### Concurrency Control

```csharp
[DisallowConcurrentExecution]
```

This attribute ensures:
- Only ONE instance of the job runs at a time
- If job is still running when next trigger fires, execution is skipped
- Prevents resource contention and data conflicts

---

## Scheduler Service

### RecordSyncSchedulerService Methods

#### 1. GetStatusAsync()
**Returns**: `SchedulerStatusDto`

Retrieves comprehensive status information:

```csharp
public class SchedulerStatusDto
{
    public bool IsRunning { get; set; }           // Scheduler is started
    public bool IsJobScheduled { get; set; }      // Job exists
    public bool IsPaused { get; set; }            // Job is paused
    public bool IsEnabled { get; set; }           // EnableSync flag
    public string CronExpression { get; set; }    // Current schedule
    public string SearchString { get; set; }       // Search criteria
    public DateTime? NextRunTime { get; set; }     // Next scheduled run
    public DateTime? PreviousRunTime { get; set; } // Last run time
    public string Message { get; set; }            // Status message
}
```

**Status Check Flow**:
```
1. Check if scheduler is started
2. Check if job exists
3. Retrieve trigger and job details
4. Extract job data (SearchString, EnableSync)
5. Calculate next/previous fire times
6. Return comprehensive status
```

#### 2. PauseJobAsync()
**Purpose**: Temporarily pause job execution

```csharp
await _scheduler.PauseTrigger(triggerKey);
```

- Trigger remains in place but won't fire
- Configuration is preserved
- Can be resumed later

#### 3. ResumeJobAsync()
**Purpose**: Resume a paused job

```csharp
await _scheduler.ResumeTrigger(triggerKey);
```

- Resumes from current schedule
- Next execution occurs at next scheduled time

#### 4. TriggerJobNowAsync()
**Purpose**: Execute job immediately (ad-hoc)

```csharp
await _scheduler.TriggerJob(jobKey);
```

- Runs outside of normal schedule
- Useful for testing or manual sync
- Subject to [DisallowConcurrentExecution] constraint

#### 5. UpdateScheduleAsync()
**Purpose**: Change cron schedule and/or search string

```csharp
public async Task<bool> UpdateScheduleAsync(string cronExpression, string? searchString = null)
```

**Process**:
1. Validate cron expression
2. Retrieve existing job
3. Update search string if provided
4. Create new trigger with new schedule
5. Reschedule job

**Validation**:
```csharp
if (!CronExpression.IsValidExpression(cronExpression))
{
    return false;
}
```

#### 6. SetSyncEnabledAsync()
**Purpose**: Enable/disable sync without removing job

```csharp
jobDetail.JobDataMap.Put("EnableSync", enabled);
await _scheduler.AddJob(jobDetail, true); // true = replace existing
```

**Use Case**: Temporarily disable sync while keeping schedule

#### 7. GetLastExecutionResultAsync()
**Purpose**: Retrieve last execution result (if still in memory)

```csharp
var currentlyExecuting = await _scheduler.GetCurrentlyExecutingJobs();
```

Note: Only returns result if job is currently executing or just completed

---

## API Controller

### REST API Endpoints

#### GET /api/RecordSyncScheduler/status
**Purpose**: Get current scheduler status

**Response**:
```json
{
  "isRunning": true,
  "isJobScheduled": true,
  "isPaused": false,
  "isEnabled": true,
  "cronExpression": "0 0 * * * ?",
  "searchString": "*",
  "nextRunTime": "2025-11-20T15:00:00Z",
  "previousRunTime": "2025-11-20T14:00:00Z",
  "message": "Job is active"
}
```

#### POST /api/RecordSyncScheduler/pause
**Purpose**: Pause the scheduled job

**Response**:
```json
{
  "success": true,
  "message": "Job paused successfully"
}
```

#### POST /api/RecordSyncScheduler/resume
**Purpose**: Resume a paused job

**Response**:
```json
{
  "success": true,
  "message": "Job resumed successfully"
}
```

#### POST /api/RecordSyncScheduler/trigger
**Purpose**: Trigger job immediately

**Response**:
```json
{
  "success": true,
  "message": "Job triggered successfully"
}
```

#### PUT /api/RecordSyncScheduler/schedule
**Purpose**: Update cron schedule and search criteria

**Request**:
```json
{
  "cronExpression": "0 0/15 * * * ?",
  "searchString": "title:report"
}
```

**Response**:
```json
{
  "success": true,
  "message": "Schedule updated to: 0 0/15 * * * ?",
  "cronExpression": "0 0/15 * * * ?",
  "searchString": "title:report"
}
```

#### PUT /api/RecordSyncScheduler/enable
**Purpose**: Enable or disable sync

**Request**:
```json
{
  "enabled": false
}
```

**Response**:
```json
{
  "success": true,
  "message": "Sync disabled",
  "enabled": false
}
```

#### GET /api/RecordSyncScheduler/cron-examples
**Purpose**: Get common cron expression examples

**Response**:
```json
{
  "examples": [
    {
      "expression": "0 0 * * * ?",
      "description": "Every hour at minute 0"
    },
    {
      "expression": "0 0/30 * * * ?",
      "description": "Every 30 minutes"
    },
    // ... more examples
  ],
  "format": "second minute hour dayOfMonth month dayOfWeek",
  "note": "Use ? for dayOfMonth or dayOfWeek when the other is specified"
}
```

---

## Configuration

### appsettings.json Configuration

```json
{
  "RecordSync": {
    "Enabled": "true",
    "CronSchedule": "0 0 * * * ?",
    "SearchString": "*",
    "PageSize": 1000,
    "MaxParallelTasks": 10,
    "CheckpointInterval": 10,
    "Description": "Cron schedule for syncing Content Manager records..."
  }
}
```

### Configuration Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| Enabled | string (bool) | "true" | Enable/disable sync on startup |
| CronSchedule | string | "0 0 * * * ?" | Quartz cron expression (every hour) |
| SearchString | string | "*" | Content Manager search criteria |
| PageSize | int | 1000 | Records per page for batch processing |
| MaxParallelTasks | int | 10 | Concurrent record processing limit |
| CheckpointInterval | int | 10 | Save checkpoint every N pages |

### Cron Expression Format (Quartz.NET)

Quartz uses a 7-field cron format:

```
 ┌───────────── second (0-59)
 │ ┌───────────── minute (0-59)
 │ │ ┌───────────── hour (0-23)
 │ │ │ ┌───────────── day of month (1-31)
 │ │ │ │ ┌───────────── month (1-12 or JAN-DEC)
 │ │ │ │ │ ┌───────────── day of week (0-6 or SUN-SAT)
 │ │ │ │ │ │ ┌───────────── year (optional, 1970-2099)
 │ │ │ │ │ │ │
 * * * * * ? *
```

**Common Expressions**:
- `0 0 * * * ?` - Every hour at minute 0
- `0 0/30 * * * ?` - Every 30 minutes
- `0 0 0/2 * * ?` - Every 2 hours
- `0 0 9-17 * * ?` - Every hour between 9 AM and 5 PM
- `0 0 12 * * ?` - Every day at noon
- `0 0 0 * * ?` - Every day at midnight
- `0 0 0 ? * MON` - Every Monday at midnight
- `0 0 0 1 * ?` - First day of every month

**Special Characters**:
- `*` - All values
- `?` - No specific value (use for day-of-month or day-of-week)
- `-` - Range (e.g., 9-17)
- `,` - List (e.g., MON,WED,FRI)
- `/` - Increments (e.g., 0/15 = every 15 units)

---

## Execution Flow

### Complete Job Execution Flow

```
┌─────────────────────────────────────────────────────────────┐
│ 1. TRIGGER FIRES (Cron Schedule or Manual)                 │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. Quartz.NET Scheduler                                     │
│    - Check [DisallowConcurrentExecution]                    │
│    - If job running: Skip this execution                    │
│    - If available: Instantiate RecordSyncJob via DI         │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. RecordSyncJob.Execute()                                  │
│    - Log job start (ID, scheduled time, actual time)        │
│    - Retrieve job data (SearchString, EnableSync)           │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ 4. Check EnableSync Flag                                    │
│    - If disabled: Log and return early                      │
│    - If enabled: Continue                                   │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ 5. IRecordEmbeddingService.ProcessAllRecordsAsync()         │
│    - Connect to Content Manager                             │
│    - Search for records (SearchString)                      │
│    - Process records in batches (PageSize)                  │
│    - Generate embeddings (parallel, MaxParallelTasks)       │
│    - Store in PostgreSQL with pgvector                      │
│    - Save checkpoints (CheckpointInterval)                  │
│    - Support cancellation (CancellationToken)               │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ 6. Calculate Metrics                                        │
│    - Record count                                           │
│    - Duration                                               │
│    - Next scheduled run                                     │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ 7. Store Execution Result                                   │
│    - Success: Store result in context.Result                │
│    - Failure: Store error details                           │
│    - Log comprehensive execution summary                    │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ 8. Quartz Scheduler Post-Processing                         │
│    - Log execution to Quartz internal storage               │
│    - Schedule next execution based on cron                  │
│    - Clean up job instance                                  │
└─────────────────────────────────────────────────────────────┘
```

### Error Handling Flow

```
┌─────────────────────────────────────────────┐
│ Exception Thrown in Execute()              │
└──────────────────┬──────────────────────────┘
                   │
          ┌────────┴────────┐
          │                 │
          ▼                 ▼
┌──────────────────┐ ┌──────────────────┐
│ Cancellation     │ │ General          │
│ Exception        │ │ Exception        │
└────────┬─────────┘ └────────┬─────────┘
         │                    │
         ▼                    ▼
┌──────────────────┐ ┌──────────────────┐
│ Log Warning      │ │ Log Error        │
│ Store result     │ │ Store result     │
│ refireImmediately│ │ refireImmediately│
│ = false          │ │ = false          │
└────────┬─────────┘ └────────┬─────────┘
         │                    │
         └────────┬───────────┘
                  │
                  ▼
┌─────────────────────────────────────────────┐
│ Quartz.NET Exception Handler               │
│ - Job execution marked as failed            │
│ - NOT rescheduled immediately               │
│ - Next execution according to cron schedule │
└─────────────────────────────────────────────┘
```

---

## Features and Capabilities

### 1. Flexible Scheduling
- **Cron-based**: Use any Quartz cron expression
- **Dynamic Updates**: Change schedule without restart
- **Multiple Patterns**: Hourly, daily, weekly, custom intervals

### 2. Job Control
- **Pause/Resume**: Temporarily halt execution
- **Manual Trigger**: Run on-demand
- **Enable/Disable**: Soft on/off switch without removing job
- **Schedule Updates**: Change frequency at runtime

### 3. Configuration Management
- **Search Criteria**: Filter which records to process
- **Job Parameters**: Modify behavior without code changes
- **Runtime Updates**: All changes via REST API

### 4. Monitoring and Observability
- **Status Queries**: Real-time job state
- **Execution History**: Track last run details
- **Timing Information**: Next/previous run times
- **Comprehensive Logging**: Structured logs via Serilog

### 5. Error Handling
- **Graceful Failures**: Errors logged, job rescheduled
- **Cancellation Support**: Clean shutdown via CancellationToken
- **Retry Logic**: Quartz can be configured for retries
- **Error Isolation**: Single job failure doesn't crash system

### 6. Performance Features
- **Non-blocking**: Runs in background without affecting API
- **Concurrency Control**: Prevents overlapping executions
- **Batch Processing**: Efficient record handling
- **Parallel Processing**: Multiple records processed concurrently

### 7. Integration
- **Dependency Injection**: Full DI support for jobs
- **ASP.NET Core**: Integrated with application lifecycle
- **Hosted Service**: Proper shutdown handling

---

## Security Considerations

### Current Status: COMMENTED OUT

The scheduler is **currently disabled** in Program.cs (lines 205-238). This means:
- No scheduled background jobs are running
- API endpoints exist but scheduler is not initialized
- Configuration is present but not active

### Authentication and Authorization

**Current State**:
- Controller has NO authentication/authorization attributes
- Endpoints are publicly accessible if scheduler is enabled
- Windows Authentication is configured globally but not enforced on scheduler endpoints

**Recommendations**:
```csharp
[Authorize] // Require authentication
public class RecordSyncSchedulerController : ControllerBase
{
    [Authorize(Roles = "Administrator")] // Role-based access
    [HttpPut("schedule")]
    public async Task<ActionResult> UpdateSchedule(...)
}
```

### API Security Risks

| Endpoint | Risk Level | Reason |
|----------|-----------|---------|
| GET /status | Low | Read-only, informational |
| POST /trigger | HIGH | Can trigger expensive operations |
| POST /pause | MEDIUM | Can disrupt operations |
| POST /resume | MEDIUM | Can resume operations |
| PUT /schedule | HIGH | Can change execution frequency |
| PUT /enable | HIGH | Can enable/disable sync |

### Recommended Security Measures

1. **Add Authorization**
   ```csharp
   [Authorize(Policy = "SchedulerAdmin")]
   ```

2. **Rate Limiting** (Already configured in appsettings.json)
   - Currently applies to all endpoints
   - Consider specific limits for scheduler endpoints

3. **Input Validation**
   - Cron expression validation (already implemented)
   - Search string sanitization
   - Maximum frequency limits

4. **Audit Logging**
   ```csharp
   _logger.LogWarning("User {User} triggered manual job execution",
       HttpContext.User.Identity.Name);
   ```

5. **IP Whitelisting**
   - Restrict scheduler API to internal networks
   - Use firewall rules or middleware

---

## Monitoring and Observability

### Logging Strategy

#### Job Execution Logs

**Start of Job**:
```log
========================================
🔄 Content Manager Record Sync Job Started
Job ID: 8c7d6e5f-4b3a-2c1d-0e9f-8a7b6c5d4e3f
Scheduled Time: 2025-11-20 14:00:00
Actual Start Time: 2025-11-20 14:00:00
========================================
```

**Processing Logs**:
```log
📋 Processing records with search criteria: *
```

**Success Logs**:
```log
========================================
✅ Content Manager Record Sync Job Completed
Records Processed: 1250
Duration: 00:05:23
Next Scheduled Run: 2025-11-20 15:00:00
========================================
```

**Cancellation Logs**:
```log
⚠️ Content Manager Record Sync Job was cancelled
Duration before cancellation: 00:02:15
```

**Error Logs**:
```log
❌ Content Manager Record Sync Job Failed
Error: Connection timeout
Duration before failure: 00:01:30
```

#### Service Operation Logs

**Pause**:
```log
⏸️ Record sync job paused
```

**Resume**:
```log
▶️ Record sync job resumed
```

**Manual Trigger**:
```log
🚀 Record sync job triggered manually
```

**Schedule Update**:
```log
✅ Schedule updated to: 0 0/15 * * * ?
```

### Metrics to Monitor

1. **Execution Metrics**
   - Records processed per run
   - Execution duration
   - Success/failure rate

2. **Timing Metrics**
   - Scheduled vs actual start time (clock drift)
   - Processing time per record
   - Average batch processing time

3. **Resource Metrics**
   - Memory usage during execution
   - Database connection pool usage
   - API call rate to Content Manager

4. **Error Metrics**
   - Exception count by type
   - Failed record count
   - Retry attempts

### Health Checks

Currently implemented:
```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DocumentProcessingDbContext>();
```

**Recommended Addition**:
```csharp
.AddCheck<QuartzSchedulerHealthCheck>("scheduler")
```

---

## Operational Guide

### Enabling the Scheduler

1. **Uncomment configuration in Program.cs** (lines 205-238)
2. **Configure appsettings.json**:
   ```json
   {
     "RecordSync": {
       "Enabled": "true",
       "CronSchedule": "0 0 * * * ?",
       "SearchString": "*"
     }
   }
   ```
3. **Restart application**
4. **Verify status**:
   ```bash
   curl http://localhost:5000/api/RecordSyncScheduler/status
   ```

### Common Operations

#### Change Schedule to Every 30 Minutes
```bash
curl -X PUT http://localhost:5000/api/RecordSyncScheduler/schedule \
  -H "Content-Type: application/json" \
  -d '{
    "cronExpression": "0 0/30 * * * ?"
  }'
```

#### Pause During Maintenance
```bash
curl -X POST http://localhost:5000/api/RecordSyncScheduler/pause
```

#### Resume After Maintenance
```bash
curl -X POST http://localhost:5000/api/RecordSyncScheduler/resume
```

#### Trigger Manual Sync
```bash
curl -X POST http://localhost:5000/api/RecordSyncScheduler/trigger
```

#### Disable Sync Temporarily
```bash
curl -X PUT http://localhost:5000/api/RecordSyncScheduler/enable \
  -H "Content-Type: application/json" \
  -d '{ "enabled": false }'
```

#### Update Search Criteria
```bash
curl -X PUT http://localhost:5000/api/RecordSyncScheduler/schedule \
  -H "Content-Type: application/json" \
  -d '{
    "cronExpression": "0 0 * * * ?",
    "searchString": "title:important"
  }'
```

### Deployment Considerations

1. **First Deployment**
   - Start with sync disabled or infrequent schedule
   - Monitor first few executions closely
   - Gradually increase frequency

2. **Production Deployment**
   - Use conservative cron schedules
   - Enable during off-peak hours initially
   - Monitor logs for errors

3. **Scaling Considerations**
   - Single instance only (due to [DisallowConcurrentExecution])
   - For multi-instance deployments, use Quartz clustering with database persistence

---

## Best Practices

### 1. Schedule Design

**DO**:
- Start with infrequent schedules (hourly or daily)
- Run during off-peak hours for large syncs
- Use appropriate cron expressions for business needs

**DON'T**:
- Schedule too frequently (under 5 minutes)
- Run large syncs during peak business hours
- Use `* * * * * ?` (every second)

### 2. Search String Optimization

**Examples**:
```
"*"                          // All records (expensive)
"title:report"               // Records with "report" in title
"recDateCreated>2025-01-01"  // Records created after date
"container:12345"            // Records in specific container
```

**Best Practices**:
- Use specific search criteria when possible
- Test search strings manually first
- Consider incremental syncs (date-based)

### 3. Error Handling

```csharp
try
{
    var result = await _schedulerService.UpdateScheduleAsync(cronExpression);
    if (!result)
    {
        // Handle validation failure
        _logger.LogWarning("Invalid cron expression: {Cron}", cronExpression);
    }
}
catch (Exception ex)
{
    // Handle infrastructure failure
    _logger.LogError(ex, "Failed to update schedule");
}
```

### 4. Monitoring

**Essential Checks**:
- Job execution success rate
- Processing duration trends
- Record count per execution
- Error logs for failed records

**Alerts**:
- Job hasn't run in expected time
- Execution duration exceeds threshold
- High error rate
- Database connection failures

### 5. Configuration Management

**Version Control**:
- Store cron schedules in configuration
- Document schedule changes
- Use environment-specific settings

**Documentation**:
```json
{
  "RecordSync": {
    "CronSchedule": "0 0 2 * * ?",  // 2 AM daily
    "_comment": "Changed from hourly to daily on 2025-11-20 due to performance"
  }
}
```

---

## Troubleshooting

### Problem: Job Not Executing

**Symptoms**:
- Status shows "IsJobScheduled: false"
- No logs appearing

**Diagnosis**:
1. Check if scheduler is enabled in Program.cs (currently commented out)
2. Verify Quartz services are registered
3. Check appsettings.json configuration

**Solution**:
```csharp
// Uncomment lines 205-238 in Program.cs
builder.Services.AddQuartz(q => { ... });
builder.Services.AddQuartzHostedService(...);
```

---

### Problem: Job Skipped

**Symptoms**:
- Logs show "Next execution skipped"
- Job status shows "IsRunning: true"

**Diagnosis**:
- Previous execution still running
- [DisallowConcurrentExecution] preventing overlap

**Solution**:
1. Wait for current execution to complete
2. Check if job is stuck (database connection timeout)
3. Consider increasing execution interval
4. Optimize ProcessAllRecordsAsync performance

---

### Problem: Invalid Cron Expression

**Symptoms**:
- Schedule update returns 400 Bad Request
- Logs show "Invalid cron expression"

**Diagnosis**:
- Cron expression validation failed

**Solution**:
1. Verify cron format: `second minute hour day month dayOfWeek`
2. Use cron examples endpoint: GET `/api/RecordSyncScheduler/cron-examples`
3. Test with online Quartz cron validator

**Common Mistakes**:
```
❌ "0 * * * *"      // 5 fields (Unix cron)
✅ "0 0 * * * ?"    // 6 fields (Quartz cron)

❌ "* * * * * *"    // Every second (performance risk)
✅ "0 0/5 * * * ?"  // Every 5 minutes

❌ "0 0 * * * *"    // Invalid (both day-of-month and day-of-week)
✅ "0 0 * * * ?"    // Use ? for one of them
```

---

### Problem: High Memory Usage

**Symptoms**:
- Application memory grows during job execution
- Out of memory exceptions

**Diagnosis**:
- Processing too many records at once
- Inefficient batch processing
- Memory leaks in embedding generation

**Solution**:
1. Reduce PageSize in configuration (default: 1000)
   ```json
   "RecordSync": {
     "PageSize": 500
   }
   ```
2. Reduce MaxParallelTasks (default: 10)
   ```json
   "RecordSync": {
     "MaxParallelTasks": 5
   }
   ```
3. Profile memory usage during execution
4. Check IRecordEmbeddingService for memory leaks

---

### Problem: Job Stuck/Never Completes

**Symptoms**:
- Job status shows "IsRunning: true" indefinitely
- No completion logs

**Diagnosis**:
- Deadlock in ProcessAllRecordsAsync
- Content Manager connection timeout
- Database connection issue

**Solution**:
1. Check Content Manager connectivity:
   ```json
   "TRIM": {
     "DataSetId": "UM",
     "WorkgroupServerUrl": "OTX-1Y0GDY3",
     "TimeoutSeconds": 300
   }
   ```
2. Check PostgreSQL connection
3. Review logs for last processed record
4. Consider adding timeout to job execution:
   ```csharp
   using var cts = new CancellationTokenSource(TimeSpan.FromHours(1));
   await service.ProcessAllRecordsAsync(searchString, cts.Token);
   ```

---

### Problem: Records Not Updating

**Symptoms**:
- Job completes successfully
- Search results show old data

**Diagnosis**:
- Embeddings not being generated
- Database write failures
- Search string not matching records

**Solution**:
1. Check failed-records logs: `logs/failed-records-*.txt`
2. Verify search string returns records:
   - Test in Content Manager UI
   - Check RecordEmbeddingService logs
3. Check PostgreSQL pgvector extension:
   ```sql
   SELECT * FROM "RecordEmbeddings" ORDER BY "LastIndexed" DESC LIMIT 10;
   ```
4. Verify Gemini API connectivity and quota

---

### Problem: Authorization Errors

**Symptoms**:
- 401 Unauthorized responses
- 403 Forbidden responses

**Diagnosis**:
- Windows Authentication enabled globally
- Scheduler endpoints require authentication
- Cookie authentication missing

**Current State**:
```csharp
// Program.cs line 42
var enableWindowsAuth = builder.Configuration.GetValue<bool>(
    "Authentication:EnableWindowsAuthentication", false);
```

**Solution**:
1. Disable Windows Auth for scheduler endpoints
2. Add [AllowAnonymous] to controller (not recommended for production)
3. Provide valid authentication credentials
4. Use service account for automated tools

---

### Problem: Performance Degradation

**Symptoms**:
- Job execution time increasing over time
- High CPU usage during execution

**Diagnosis**:
- Growing record count
- Inefficient queries
- Resource contention

**Solutions**:

1. **Optimize Search String** (incremental sync):
   ```json
   {
     "searchString": "recDateModified>[lastSyncDate]"
   }
   ```

2. **Adjust Batch Size**:
   ```json
   {
     "PageSize": 500,
     "MaxParallelTasks": 5
   }
   ```

3. **Schedule During Off-Peak**:
   ```json
   {
     "CronSchedule": "0 0 2 * * ?"  // 2 AM daily
   }
   ```

4. **Monitor Database Performance**:
   - Check query execution times
   - Analyze PostgreSQL slow query log
   - Verify pgvector index exists

---

### Problem: Scheduler Service Null Reference

**Symptoms**:
- Controller throws NullReferenceException
- `_schedulerService` is null

**Diagnosis**:
- RecordSyncSchedulerService not registered in DI
- Service registration commented out

**Solution**:
Uncomment in Program.cs line 237:
```csharp
builder.Services.AddSingleton<RecordSyncSchedulerService>();
```

---

## Diagnostic Commands

### Check Scheduler Status
```bash
curl http://localhost:5000/api/RecordSyncScheduler/status | jq
```

### Check Database Embeddings
```sql
-- PostgreSQL
SELECT COUNT(*) FROM "RecordEmbeddings";
SELECT MAX("LastIndexed") FROM "RecordEmbeddings";
```

### Check Logs
```bash
# Application logs
tail -f logs/documentprocessing-*.txt

# Failed records
tail -f logs/failed-records-*.txt

# Filter for scheduler logs
grep "Record Sync Job" logs/documentprocessing-*.txt
```

### Check Health
```bash
curl http://localhost:5000/health
```

---

## Future Enhancements

### 1. Job History Persistence
Currently, execution history is only in memory. Consider:
- Storing JobExecutionResult in database
- Historical trend analysis
- Job execution dashboard

### 2. Multiple Jobs
Support for different sync jobs:
- Full sync job (all records)
- Incremental sync job (recent changes)
- Cleanup job (remove old embeddings)

### 3. Advanced Scheduling
- Time zone aware scheduling
- Business hours only execution
- Conditional execution (only if Content Manager has changes)

### 4. Notification System
- Email notifications on job failure
- Slack/Teams integration
- Webhook callbacks for monitoring systems

### 5. Job Clustering
For multi-instance deployments:
- Quartz.NET clustering with database persistence
- Distributed job execution
- Load balancing across instances

### 6. Metrics Export
- Prometheus metrics endpoint
- Grafana dashboards
- Application Insights integration

### 7. Job Chaining
- Pre-job validation tasks
- Post-job cleanup tasks
- Conditional job execution

---

## Summary

The Record Sync Scheduler is a well-architected background job system built on Quartz.NET that provides:

**Strengths**:
- Flexible cron-based scheduling
- Full REST API control
- Comprehensive logging
- Error handling and resilience
- Non-blocking asynchronous execution
- Dependency injection support

**Current Limitations**:
- **Currently disabled** (commented out in Program.cs)
- No authentication on API endpoints
- No persistent job history
- Limited to single instance (no clustering)
- No built-in monitoring dashboard

**Recommended Actions**:
1. **Security**: Add authorization to scheduler endpoints
2. **Monitoring**: Implement health checks and metrics
3. **Persistence**: Store job execution history
4. **Documentation**: Create operational runbooks
5. **Testing**: Add integration tests for scheduler operations

**Use Cases**:
- Automated nightly sync of Content Manager records
- Periodic embedding regeneration
- Incremental updates during business hours
- On-demand sync for testing

The system is production-ready once security measures are implemented and the configuration is uncommented. It provides a solid foundation for automated document processing and embedding generation.

---

## File References

| Component | File Path | Lines |
|-----------|-----------|-------|
| Job Implementation | DocumentProcessingAPI.Infrastructure\Jobs\RecordSyncJob.cs | 1-133 |
| Scheduler Service | DocumentProcessingAPI.Infrastructure\Services\RecordSyncSchedulerService.cs | 1-264 |
| API Controller | DocumentProcessingAPI.API\Controllers\RecordSyncSchedulerController.cs | 1-245 |
| Configuration | DocumentProcessingAPI.API\Program.cs | 205-238 |
| Settings | DocumentProcessingAPI.API\appsettings.json | 87-95 |

---

## Contact and Support

For issues or questions:
- Review logs in `logs/documentprocessing-*.txt`
- Check failed records in `logs/failed-records-*.txt`
- Use `/api/RecordSyncScheduler/status` for current state
- Consult Quartz.NET documentation: https://www.quartz-scheduler.net/

---

**Document Version**: 1.0
**Last Updated**: 2025-11-20
**Author**: Deep Analysis Report
