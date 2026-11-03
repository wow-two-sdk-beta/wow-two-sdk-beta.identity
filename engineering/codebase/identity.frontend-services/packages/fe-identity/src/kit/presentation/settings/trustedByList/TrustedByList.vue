<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { useLocaleFormatters } from '@wow-two-beta/fe/foundation/i18n';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { computed, onMounted, onScopeDispose, shallowRef } from 'vue';
import { IdentityErrorCodeConstants, type TrustedByDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { isIdentityCode } from '../../../account/AccountProblems';
import { useAccountClient, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';
import { personLabel } from '../PersonLabel';

/**
 * Lists the accounts whose sign-in codes the signed-in person receives as their trusted contact ("People who trust
 * you"), and lets them leave one after a confirm step. Leaving shows only where `canLeave` says so: the last contact of an
 * account with no phone of its own stays, and one line says why. Nothing renders while the service has no such route
 * (`404`) or nobody trusts the person.
 */
defineOptions({ name: 'TrustedByList' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
  }>(),
  { client: undefined, baseUrl: undefined },
);

const emit = defineEmits<{
  /** Fires once the person stopped being an account's trusted contact. */
  left: [userId: string];
}>();

const client = useAccountClient(props);
const t = useAccountText();
const formatters = useLocaleFormatters();
const accounts = shallowRef<ReadonlyArray<TrustedByDto>>([]);
const failure = shallowRef<AppError | null>(null);
const confirming = shallowRef<string | null>(null);
const leaving = shallowRef<string | null>(null);
const leaveFailure = shallowRef<AppError | null>(null);
const controller = new AbortController();

const isShown = computed(() => failure.value !== null || accounts.value.length > 0);

onMounted(async () => {
  const result = await client.getTrustedBy(controller.signal);
  if (controller.signal.aborted) return;
  if (result.ok) accounts.value = result.value;
  else if (result.failure.status !== 404) failure.value = result.failure;
});
onScopeDispose(() => controller.abort());

const date = (value: string) => {
  const at = Date.parse(value);
  return Number.isNaN(at) ? '' : formatters.value.date(at, { dateStyle: 'medium' });
};

function askToLeave(userId: string) {
  leaveFailure.value = null;
  confirming.value = userId;
}

async function leave(account: TrustedByDto) {
  if (leaving.value) return;
  leaving.value = account.userId;
  leaveFailure.value = null;
  const result = await client.leaveTrustedContact(account.userId);
  leaving.value = null;
  confirming.value = null;
  if (!result.ok) {
    leaveFailure.value = result.failure;
    return;
  }
  accounts.value = accounts.value.filter(({ userId }) => userId !== account.userId);
  emit('left', account.userId);
}
</script>

<template>
  <section v-if="isShown" class="flex flex-col gap-3" :aria-label="t('TrustedBy.title', 'People who trust you')">
    <h2 class="text-lg font-semibold">{{ t('TrustedBy.title', 'People who trust you') }}</h2>
    <FailureAlert :failure="failure" />
    <template v-if="accounts.length">
      <p class="text-sm text-muted-foreground">
        {{ t('TrustedBy.intro', 'You receive the sign-in codes of these accounts and can sign in to them.') }}
      </p>
      <ul class="divide-y divide-border" :aria-label="t('TrustedBy.title', 'People who trust you')">
        <li v-for="account in accounts" :key="account.userId" class="flex flex-col gap-2 py-3">
          <div class="flex flex-wrap items-center gap-3">
            <div class="min-w-0 flex-1">
              <p class="truncate font-medium">{{ personLabel(t, account.displayName) }}</p>
              <p v-if="date(account.createdAt)" class="truncate text-sm text-muted-foreground">
                {{ t('TrustedBy.since', 'Since {date}', { date: date(account.createdAt) }) }}
              </p>
            </div>
            <Button
              v-if="account.canLeave && confirming !== account.userId"
              type="button"
              variant="ghost"
              tone="danger"
              :is-disabled="leaving !== null"
              @click="askToLeave(account.userId)"
            >
              {{ t('TrustedBy.leave', 'Leave') }}
            </Button>
          </div>
          <p v-if="!account.canLeave" class="text-sm text-muted-foreground">
            {{
              t(
                'TrustedBy.cannotLeave',
                'You are the only contact of an account with no phone of its own, so you stay.',
              )
            }}
          </p>
          <div
            v-if="confirming === account.userId"
            class="flex flex-wrap items-center gap-2 rounded-md bg-muted p-3 text-sm"
            role="group"
          >
            <p class="flex-1">
              {{
                t('TrustedBy.confirmLeave', 'Stop receiving the sign-in codes of {name}?', {
                  name: personLabel(t, account.displayName),
                })
              }}
            </p>
            <Button type="button" tone="danger" :is-loading="leaving === account.userId" @click="leave(account)">
              {{ t('TrustedBy.leaveConfirmed', 'Yes, leave') }}
            </Button>
            <Button
              type="button"
              variant="ghost"
              tone="neutral"
              :is-disabled="leaving !== null"
              @click="confirming = null"
            >
              {{ t('TrustedBy.cancel', 'Cancel') }}
            </Button>
          </div>
        </li>
      </ul>
      <AlertSimple
        v-if="isIdentityCode(leaveFailure, IdentityErrorCodeConstants.lastTrustedContact)"
        severity="warning"
      >
        {{
          t('TrustedBy.cannotLeave', 'You are the only contact of an account with no phone of its own, so you stay.')
        }}
      </AlertSimple>
      <FailureAlert v-else :failure="leaveFailure" />
    </template>
  </section>
</template>
