import { describe, expect, it } from 'vitest';
import { readHostedGreetingRequest } from '../src/application/greetings/ReadHostedGreetingRequest';

describe('hosted greeting request', () => {
  it('reads the name and an http(s) return URL', () => {
    expect(readHostedGreetingRequest({ name: 'Ann', return_url: 'https://app.test/done' })).toEqual({
      name: 'Ann',
      returnUrl: 'https://app.test/done',
    });
  });

  it('takes the first value of a repeated parameter', () => {
    expect(readHostedGreetingRequest({ name: [null, 'Ann', 'Bob'] })).toEqual({ name: 'Ann', returnUrl: null });
  });

  it.each(['javascript:alert(1)', 'not a url', '/relative'])('drops the return URL %s', (returnUrl) => {
    expect(readHostedGreetingRequest({ return_url: returnUrl })).toEqual({ name: '', returnUrl: null });
  });
});
