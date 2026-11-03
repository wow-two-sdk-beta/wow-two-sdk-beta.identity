<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { useLocaleFormatters } from '@wow-two-beta/fe/foundation/i18n';
import { Badge } from '@wow-two-beta/fe/presentation/display';
import { computed, onMounted, onScopeDispose, shallowRef } from 'vue';
import type { SessionDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';

/**
 * Lists the signed-in account's live sessions, newest first as the service gives them: where each signed in from, when,
 * and, for a session started with a trusted contact's code, the contact it went through. Read-only. Nothing renders
 * while the service keeps no session list (`404`) or the list is empty.
 */
defineOptions({ name: 'SessionList' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
  }>(),
  { client: undefined, baseUrl: undefined },
);

const client = useAccountClient(props);
const t = useAccountText();
const formatters = useLocaleFormatters();
const sessions = shallowRef<ReadonlyArray<SessionDto>>([]);
const failure = shallowRef<AppError | null>(null);
const controller = new AbortController();

const isShown = computed(() => failure.value !== null || sessions.value.length > 0);

onMounted(async () => {
  const result = await client.getSessions(controller.signal);
  if (controller.signal.aborted) return;
  if (result.ok) sessions.value = result.value;
  else if (result.failure.status !== 404) failure.value = result.failure;
});
onScopeDispose(() => controller.abort());

const when = (value: string) => {
  const at = Date.parse(value);
  return Number.isNaN(at) ? '' : formatters.value.date(at, { dateStyle: 'medium', timeStyle: 'short' });
};

/** The session's places and times beside each other: the network, when it signed in and when it was last used. */
function details(session: SessionDto): string {
  return [
    session.ipMasked,
    when(session.createdAt) ? t('Sessions.signedIn', 'Signed in {date}', { date: when(session.createdAt) }) : '',
    when(session.lastSeenAt) ? t('Sessions.lastSeen', 'Last seen {date}', { date: when(session.lastSeenAt) }) : '',
  ]
    .filter((part) => !!part)
    .join(' · ');
}
</script>

<template>
  <section v-if="isShown" class="flex flex-col gap-3" :aria-label="t('Sessions.title', 'Sessions')">
    <h2 class="text-lg font-semibold">{{ t('Sessions.title', 'Sessions') }}</h2>
    <FailureAlert :failure="failure" />
    <ul v-if="sessions.length" class="divide-y divide-border" :aria-label="t('Sessions.title', 'Sessions')">
      <li v-for="session in sessions" :key="session.id" class="flex flex-col gap-1 py-3">
        <div class="flex flex-wrap items-center gap-2">
          <p class="min-w-0 flex-1 truncate font-medium">
            {{ session.userAgent ?? t('Sessions.unknownDevice', 'Unknown device') }}
          </p>
          <Badge v-if="session.isCurrent">{{ t('Sessions.current', 'This session') }}</Badge>
        </div>
        <p class="truncate text-sm text-muted-foreground">{{ details(session) }}</p>
        <p v-if="session.trustedContactDisplayName" class="text-sm text-muted-foreground">
          {{
            t('Sessions.throughContact', 'Signed in through {contact}', { contact: session.trustedContactDisplayName })
          }}
        </p>
      </li>
    </ul>
  </section>
</template>
