import { onMounted, onScopeDispose, shallowRef } from 'vue';
import type { SystemStatus } from '../../domain/system/SystemStatus';
import { systemApi } from '../../integration/system';

/** Loads the backend's status for this component's lifetime. */
export function useSystemStatus() {
  const controller = new AbortController();
  const status = shallowRef<SystemStatus | null>(null);

  onMounted(async () => {
    const result = await systemApi.getStatus(controller.signal);
    if (!controller.signal.aborted && result.ok) status.value = result.value;
  });
  onScopeDispose(() => controller.abort());

  return { status };
}
