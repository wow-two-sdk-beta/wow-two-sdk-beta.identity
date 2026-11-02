<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { Button, CopyButton } from '@wow-two-beta/fe/presentation/actions';
import { Card } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { computed, onMounted, onScopeDispose, shallowRef, watch } from 'vue';
import type { InvitationCreatedDto, InvitationDto } from '../../../../contracts';
import { useAccountText } from '../../../../kit/account/hooks';
import { FailureAlert } from '../../../../kit/presentation/feedback/failureAlert';
import type { OrganizationClient } from '../../../organizations';
import { useOrganizationClient } from '../../../organizations/hooks';
import InvitationForm from './InvitationForm.vue';
import InvitationRow from './InvitationRow.vue';

/**
 * Lists an organization's invitations with where each stands, for its owners and admins: makes a link with any catalog
 * role and an optional phone number, shows that link once with a copy button, and revokes a pending one; a refusal shows
 * in the panel.
 */
defineOptions({ name: 'InvitationsPanel' });

const props = withDefaults(
  defineProps<{
    /** The organization whose invitations the panel manages. */
    organizationId: string;
    /** The organization API to call; built from `baseUrl` when omitted. */
    client?: OrganizationClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** The ISO codes of the countries an invited phone may come from, in display order; every country when omitted. */
    countries?: ReadonlyArray<string>;
    /** The ISO code the phone field selects first; the first allowed country when omitted. */
    defaultCountry?: string;
  }>(),
  { client: undefined, baseUrl: undefined, countries: undefined, defaultCountry: undefined },
);

const emit = defineEmits<{
  /** Fires with a new invitation and its link. */
  created: [created: InvitationCreatedDto];
  /** Fires once an invitation is withdrawn. */
  revoked: [invitationId: string];
}>();

const client = useOrganizationClient(props);
const t = useAccountText();
const isLoading = shallowRef(true);
const invitations = shallowRef<ReadonlyArray<InvitationDto>>([]);
const failure = shallowRef<AppError | null>(null);
/** The link or token of the invitation made last; it shows until dismissed, since no later answer carries it. */
const created = shallowRef<InvitationCreatedDto | null>(null);
const shared = computed(() => created.value?.link ?? created.value?.token ?? '');
const sharedNote = computed(() =>
  created.value?.link
    ? t('InvitationsPanel.linkOnce', 'Send this link to the person you invite. It shows only this once.')
    : t('InvitationsPanel.tokenOnce', 'Send this invitation token to the person you invite. It shows only this once.'),
);
let controller: AbortController | null = null;

/** Loads the invitations, dropping an answer a newer load replaced. */
async function load() {
  controller?.abort();
  const current = new AbortController();
  controller = current;
  isLoading.value = true;
  const result = await client.listInvitations(props.organizationId, current.signal);
  if (current.signal.aborted) return;
  isLoading.value = false;
  invitations.value = result.ok ? result.value : [];
  failure.value = result.ok ? null : result.failure;
}

function add(value: InvitationCreatedDto) {
  created.value = value;
  invitations.value = [value.invitation, ...invitations.value.filter(({ id }) => id !== value.invitation.id)];
  emit('created', value);
}

function revoked(invitationId: string) {
  invitations.value = invitations.value.map((invitation) =>
    invitation.id === invitationId ? { ...invitation, status: 'revoked' } : invitation,
  );
  emit('revoked', invitationId);
}

onMounted(load);
watch(
  () => props.organizationId,
  () => {
    created.value = null;
    void load();
  },
);
onScopeDispose(() => controller?.abort());

defineExpose({
  /** Loads the invitations again. */
  reload: load,
});
</script>

<template>
  <Card class="flex flex-col gap-4 p-6">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-base font-semibold">{{ t('InvitationsPanel.title', 'Invitations') }}</h2>
      <Button type="button" variant="outline" tone="neutral" size="sm" :is-loading="isLoading" @click="load">
        {{ t('InvitationsPanel.reload', 'Reload') }}
      </Button>
    </div>

    <InvitationForm
      :organization-id="organizationId"
      :client="client"
      :countries="countries"
      :default-country="defaultCountry"
      @created="add"
    />

    <AlertSimple v-if="created" severity="success">
      <div class="flex flex-col gap-2">
        <p>{{ sharedNote }}</p>
        <div class="flex items-center gap-2">
          <code class="min-w-0 flex-1 break-all rounded bg-muted px-2 py-1 text-xs">{{ shared }}</code>
          <CopyButton
            :text="shared"
            :aria-label="t('InvitationsPanel.copy', 'Copy')"
            :copied-aria-label="t('InvitationsPanel.copied', 'Copied')"
          />
        </div>
        <div>
          <Button type="button" variant="ghost" tone="neutral" size="sm" @click="created = null">
            {{ t('InvitationsPanel.done', 'Done') }}
          </Button>
        </div>
      </div>
    </AlertSimple>

    <FailureAlert :failure="failure" />
    <ul
      v-if="invitations.length"
      class="divide-y divide-border"
      :aria-label="t('InvitationsPanel.title', 'Invitations')"
    >
      <InvitationRow
        v-for="invitation in invitations"
        :key="invitation.id"
        :invitation="invitation"
        :organization-id="organizationId"
        :client="client"
        @revoked="revoked(invitation.id)"
      />
    </ul>
    <p v-else-if="!isLoading && !failure" class="text-sm text-muted-foreground">
      {{ t('InvitationsPanel.empty', 'No invitations yet.') }}
    </p>
  </Card>
</template>
