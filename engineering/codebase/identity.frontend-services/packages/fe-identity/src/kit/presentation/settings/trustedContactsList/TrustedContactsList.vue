<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { useLocaleFormatters } from '@wow-two-beta/fe/foundation/i18n';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { computed, onMounted, onScopeDispose, shallowRef } from 'vue';
import { IdentityErrorCodeConstants, type TrustedContactDto, type TrustedContactsDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { isIdentityCode } from '../../../account/AccountProblems';
import { useAccountClient, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';
import { personLabel } from '../PersonLabel';

/**
 * Lists the people who receive the signed-in account's sign-in codes, its trusted contacts, and revokes one after a
 * confirm step, which ends every session started through them. Revoking shows only while the session was signed in with
 * the account's own phone (`canRevoke`); otherwise one line says how to get there. Nothing renders while the service has
 * no trusted contacts route (`404`), nor, unless `isEmptyShown`, while the account has none.
 */
defineOptions({ name: 'TrustedContactsList' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** Whether the block shows when the account has no trusted contact, with a line saying so. */
    isEmptyShown?: boolean;
  }>(),
  { client: undefined, baseUrl: undefined, isEmptyShown: true },
);

const emit = defineEmits<{
  /** Fires once a trusted contact is revoked. */
  revoked: [userId: string];
}>();

const client = useAccountClient(props);
const t = useAccountText();
const formatters = useLocaleFormatters();
const contacts = shallowRef<TrustedContactsDto | null>(null);
const failure = shallowRef<AppError | null>(null);
const isAbsent = shallowRef(false);
/** The contact whose revoke awaits the confirm step, and the one whose request runs. */
const confirming = shallowRef<string | null>(null);
const revoking = shallowRef<string | null>(null);
const revokeFailure = shallowRef<AppError | null>(null);
const controller = new AbortController();

const items = computed<ReadonlyArray<TrustedContactDto>>(() => contacts.value?.items ?? []);
const isShown = computed(
  () =>
    !isAbsent.value &&
    (failure.value !== null || (contacts.value !== null && (props.isEmptyShown || items.value.length > 0))),
);

onMounted(async () => {
  const result = await client.getTrustedContacts(controller.signal);
  if (controller.signal.aborted) return;
  if (result.ok) contacts.value = result.value;
  else if (result.failure.status === 404) isAbsent.value = true;
  else failure.value = result.failure;
});
onScopeDispose(() => controller.abort());

const date = (value: string) => {
  const at = Date.parse(value);
  return Number.isNaN(at) ? '' : formatters.value.date(at, { dateStyle: 'medium' });
};

function askToRevoke(userId: string) {
  revokeFailure.value = null;
  confirming.value = userId;
}

async function revoke(contact: TrustedContactDto) {
  if (revoking.value) return;
  revoking.value = contact.userId;
  revokeFailure.value = null;
  const result = await client.revokeTrustedContact(contact.userId);
  revoking.value = null;
  confirming.value = null;
  if (!result.ok) {
    revokeFailure.value = result.failure;
    return;
  }
  if (contacts.value) {
    contacts.value = {
      ...contacts.value,
      items: contacts.value.items.filter(({ userId }) => userId !== contact.userId),
    };
  }
  emit('revoked', contact.userId);
}
</script>

<template>
  <section v-if="isShown" class="flex flex-col gap-3" :aria-label="t('TrustedContacts.title', 'Trusted contacts')">
    <h2 class="text-lg font-semibold">{{ t('TrustedContacts.title', 'Trusted contacts') }}</h2>
    <FailureAlert :failure="failure" />
    <template v-if="contacts">
      <p class="text-sm text-muted-foreground">
        {{
          t(
            'TrustedContacts.intro',
            'These people receive your sign-in codes. That gives them full access to your account.',
          )
        }}
      </p>
      <ul
        v-if="items.length"
        class="divide-y divide-border"
        :aria-label="t('TrustedContacts.title', 'Trusted contacts')"
      >
        <li v-for="contact in items" :key="contact.userId" class="flex flex-col gap-2 py-3">
          <div class="flex flex-wrap items-center gap-3">
            <div class="min-w-0 flex-1">
              <p class="truncate font-medium">{{ personLabel(t, contact.displayName) }}</p>
              <p class="truncate text-sm text-muted-foreground">
                <template v-if="contact.phoneHint">{{ contact.phoneHint }}</template>
                <template v-if="contact.phoneHint && date(contact.createdAt)"> · </template>
                <template v-if="date(contact.createdAt)">
                  {{ t('TrustedContacts.added', 'Added {date}', { date: date(contact.createdAt) }) }}
                </template>
              </p>
            </div>
            <Badge v-if="!contact.isDeliverable">
              {{ t('TrustedContacts.notDeliverable', 'Cannot receive codes now') }}
            </Badge>
            <Button
              v-if="contacts.canRevoke && confirming !== contact.userId"
              type="button"
              variant="ghost"
              tone="danger"
              :is-disabled="revoking !== null"
              @click="askToRevoke(contact.userId)"
            >
              {{ t('TrustedContacts.revoke', 'Revoke') }}
            </Button>
          </div>
          <div
            v-if="confirming === contact.userId"
            class="flex flex-wrap items-center gap-2 rounded-md bg-muted p-3 text-sm"
            role="group"
          >
            <p class="flex-1">
              {{
                t('TrustedContacts.confirmRevoke', 'Revoke {name}? Sessions started through them end at once.', {
                  name: personLabel(t, contact.displayName),
                })
              }}
            </p>
            <Button type="button" tone="danger" :is-loading="revoking === contact.userId" @click="revoke(contact)">
              {{ t('TrustedContacts.revokeConfirmed', 'Yes, revoke') }}
            </Button>
            <Button
              type="button"
              variant="ghost"
              tone="neutral"
              :is-disabled="revoking !== null"
              @click="confirming = null"
            >
              {{ t('TrustedContacts.cancel', 'Cancel') }}
            </Button>
          </div>
        </li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">
        {{ t('TrustedContacts.empty', 'You have no trusted contacts.') }}
      </p>
      <p v-if="items.length && !contacts.canRevoke" class="text-sm text-muted-foreground">
        {{ t('TrustedContacts.cannotRevoke', 'To revoke a trusted contact, sign in with your own phone.') }}
      </p>
      <AlertSimple
        v-if="isIdentityCode(revokeFailure, IdentityErrorCodeConstants.mainPhoneRequired)"
        severity="warning"
      >
        {{ t('TrustedContacts.cannotRevoke', 'To revoke a trusted contact, sign in with your own phone.') }}
      </AlertSimple>
      <FailureAlert v-else :failure="revokeFailure" />
    </template>
  </section>
</template>
