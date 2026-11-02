import { flushPromises, mount, type DOMWrapper } from '@vue/test-utils';
import { afterEach } from 'vitest';

const wrappers: ReturnType<typeof mount>[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

/** Mounts a kit component into the document, so focus and form submission behave as in the browser. */
export function render(...args: Parameters<typeof mount>) {
  const [component, options] = args;
  const wrapper = mount(component, { attachTo: document.body, ...options });
  wrappers.push(wrapper);
  return wrapper;
}

/** Finds the button whose text is `text`, in a mounted component or one of its elements. */
export function button(wrapper: ReturnType<typeof mount> | DOMWrapper<Element>, text: string) {
  const found = wrapper.findAll('button').find((candidate) => candidate.text() === text);
  if (!found) throw new Error(`No button "${text}" in: ${wrapper.text()}`);
  return found;
}

/** Opens the select whose trigger is labelled `label` and picks the option whose text is `option`. */
export async function pickOption(wrapper: ReturnType<typeof mount>, label: string, option: string) {
  await wrapper.get(`button[aria-label="${label}"]`).trigger('click');
  await flushPromises();
  const found = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find(
    (candidate) => candidate.textContent?.trim() === option,
  );
  if (!found) throw new Error(`No option "${option}" in the select "${label}"`);
  found.click();
  await flushPromises();
}
