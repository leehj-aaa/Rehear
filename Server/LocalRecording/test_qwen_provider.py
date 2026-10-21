import json
import os
import unittest
from unittest.mock import patch
import httpx
from pydantic import BaseModel
import qwen_provider


class Question(BaseModel):
    question: str


class QwenProviderTests(unittest.TestCase):
    settings = {'QWEN_API_KEY': 'test-key',
                'QWEN_BASE_URL': 'https://dashscope-intl.aliyuncs.com/compatible-mode/v1',
                'QWEN_MODEL': 'qwen-flash'}

    def test_sends_to_qwen_and_validates_json(self):
        def reply(request):
            self.assertEqual(request.url.host, 'dashscope-intl.aliyuncs.com')
            body = json.loads(request.content)
            self.assertEqual(body['model'], 'qwen-flash')
            self.assertFalse(body['enable_thinking'])
            self.assertEqual(body['response_format'], {'type': 'json_object'})
            self.assertIn('실제 답변', body['messages'][1]['content'])
            return httpx.Response(200, json={'choices': [{'finish_reason': 'stop',
                'message': {'content': '{"question":"그 결과를 어떻게 검증했나요?"}'}}]})
        client = httpx.Client(transport=httpx.MockTransport(reply))
        with patch.dict(os.environ, self.settings), patch.object(qwen_provider.httpx, 'Client', return_value=client):
            result = qwen_provider.generate(Question, 'Ask a relevant question.', {'answer': '실제 답변'})
        self.assertIn('검증', result.question)

    def test_wrong_response_is_rejected_without_exposing_body(self):
        client = httpx.Client(transport=httpx.MockTransport(lambda request: httpx.Response(200,
            json={'choices': [{'finish_reason': 'stop', 'message': {'content': '{"secret":"private"}'}}]})))
        with patch.dict(os.environ, self.settings), patch.object(qwen_provider.httpx, 'Client', return_value=client):
            with self.assertRaisesRegex(RuntimeError, '^Qwen response did not match the required JSON format.$'):
                qwen_provider.generate(Question, 'Ask.', {})

    def test_rejects_openai_endpoint(self):
        with patch.dict(os.environ, {'QWEN_BASE_URL': 'https://api.openai.com/v1'}):
            with self.assertRaises(RuntimeError):
                qwen_provider.endpoint()


if __name__ == '__main__':
    unittest.main()
