import { PatternMode } from './enums';
import { StallRule, SlowRule } from './queue-rule.model';
import type { JobSchedule } from '@shared/utils/schedule.util';

// Re-export for backward compatibility
export type { JobSchedule } from '@shared/utils/schedule.util';
export { ScheduleOptions } from '@shared/utils/schedule.util';

export interface FailedImportConfig {
  maxStrikes: number;
  ignorePrivate: boolean;
  deletePrivate: boolean;
  skipIfNotFoundInClient: boolean;
  patterns: string[];
  patternMode?: PatternMode;
  changeCategory: boolean;
}

export interface AiImportConfig {
  enabled: boolean;
  confidenceThreshold: number;
  timeoutSeconds: number;
  tickBudgetSeconds: number;
  breakerFailureThreshold: number;
  breakerCooldownMinutes: number;
  skipBudget: number;
  decisionCacheTtlHours: number;
  ollamaUrl: string;
  model: string;
  targetMessagePrefix: string;
}

export interface TestOllamaConnectionResult {
  message: string;
  models?: string[];
}

export interface QueueCleanerConfig {
  enabled: boolean;
  cronExpression: string;
  useAdvancedScheduling: boolean;
  jobSchedule?: JobSchedule;
  ignoredDownloads: string[];
  processNoContentId: boolean;
  failedImport: FailedImportConfig;
  aiImport: AiImportConfig;
  downloadingMetadataMaxStrikes: number;
  stallRules?: StallRule[];
  slowRules?: SlowRule[];
}
