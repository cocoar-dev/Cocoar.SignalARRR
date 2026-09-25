import { describe, it, expect } from 'vitest';
import { HARRRErrorCodes, normalizeErrorCode, parseHARRRError } from '../../src/models/harrr-error.js';

/** The error envelope carries a machine-readable `Code`, as in the .NET, Swift and Kotlin clients. */
describe('HARRRError', () => {
  it('parses the versioned envelope with its code', () => {
    const error = parseHARRRError(new Error(
      'An unexpected error occurred invoking \'X\' on the server. HARRRException: ' +
        '{"Version":1,"Code":"unauthorized","Type":"Cocoar.SignalARRR.Server.HARRRException","Message":"Not allowed."}',
    ));
    expect(error.Code).toBe('unauthorized');
    expect(error.Version).toBe(1);
    expect(error.Type).toBe('Cocoar.SignalARRR.Server.HARRRException');
    expect(error.Message).toBe('Not allowed.');
  });

  it('accepts an envelope that carries only a code', () => {
    const error = parseHARRRError(new Error('{"Code":"timeout"}'));
    expect(error.Code).toBe('timeout');
    expect(error.Type).toBe('Error');
    expect(error.Message).toBe('');
  });

  it('keeps accepting the Type/Message shape without a code', () => {
    const error = parseHARRRError(new Error('{"Type":"System.ArgumentException","Message":"bad"}'));
    expect(error.Code).toBeUndefined();
    expect(error.Type).toBe('System.ArgumentException');
  });

  it('folds unknown and missing codes to internal', () => {
    expect(normalizeErrorCode('unauthorized')).toBe(HARRRErrorCodes.Unauthorized);
    expect(normalizeErrorCode('room_full')).toBe(HARRRErrorCodes.Internal);
    expect(normalizeErrorCode(undefined)).toBe(HARRRErrorCodes.Internal);
  });
});
