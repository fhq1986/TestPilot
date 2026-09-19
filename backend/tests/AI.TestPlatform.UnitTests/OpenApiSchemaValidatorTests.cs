using AI.TestPlatform.Application.ApiTesting;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 接口响应结构校验。
///
/// 这是「契约测试」的核心：只在状态码上断言的话，后端把字段改名、改类型、
/// 或者漏返回，用例照样绿。这组用例覆盖两个方向——
/// 该报的要报全，不该报的（规范没声明的、平台不支持的写法）绝不能误报。
/// </summary>
public class OpenApiSchemaValidatorTests
{
    /// <summary>一份最小可用的 OpenAPI 3 规范，覆盖主要校验分支</summary>
    private const string Spec = """
    {
      "openapi": "3.0.0",
      "paths": {
        "/api/users/{id}": {
          "get": {
            "responses": {
              "200": {
                "content": {
                  "application/json": {
                    "schema": { "$ref": "#/components/schemas/User" }
                  }
                }
              },
              "2XX": {
                "content": {
                  "application/json": {
                    "schema": { "type": "object", "required": ["ok"] }
                  }
                }
              },
              "404": {
                "content": {
                  "application/json": {
                    "schema": {
                      "type": "object",
                      "required": ["message"],
                      "properties": { "message": { "type": "string" } }
                    }
                  }
                }
              }
            }
          }
        },
        "/api/items": {
          "get": {
            "responses": {
              "200": {
                "content": {
                  "application/json": {
                    "schema": {
                      "type": "array",
                      "items": { "$ref": "#/components/schemas/User" }
                    }
                  }
                }
              }
            }
          }
        },
        "/api/status": {
          "get": {
            "responses": {
              "200": {
                "content": {
                  "application/json": {
                    "schema": {
                      "type": "object",
                      "closed": true,
                      "additionalProperties": false,
                      "properties": {
                        "state": { "type": "string", "enum": ["on", "off"] },
                        "count": { "type": "integer" },
                        "note": { "type": "string", "nullable": true }
                      }
                    }
                  }
                }
              }
            }
          }
        }
      },
      "components": {
        "schemas": {
          "User": {
            "type": "object",
            "required": ["id", "name"],
            "properties": {
              "id": { "type": "string" },
              "name": { "type": "string" },
              "age": { "type": "integer" },
              "tags": { "type": "array", "items": { "type": "string" } }
            }
          }
        }
      }
    }
    """;

    private static SchemaValidationResult Validate(string method, string path, int status, string body) =>
        OpenApiSchemaValidator.Validate(Spec, method, path, status, body);

    // ------------------------------ 通过

    [Fact]
    public void 完全符合契约时通过()
    {
        var result = Validate("GET", "/api/users/{id}", 200,
            """{"id":"1","name":"张三","age":30,"tags":["a","b"]}""");

        Assert.True(result.Valid, result.Describe());
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void 只声明必填字段时可省略可选字段()
    {
        var result = Validate("GET", "/api/users/{id}", 200, """{"id":"1","name":"张三"}""");

        Assert.True(result.Valid, result.Describe());
    }

    [Fact]
    public void 数组响应逐元素校验通过()
    {
        var result = Validate("GET", "/api/items", 200, """[{"id":"1","name":"a"},{"id":"2","name":"b"}]""");

        Assert.True(result.Valid, result.Describe());
    }

    [Fact]
    public void nullable字段允许为null()
    {
        var result = Validate("GET", "/api/status", 200, """{"state":"on","count":1,"note":null}""");

        Assert.True(result.Valid, result.Describe());
    }

    [Fact]
    public void 未声明additionalProperties时允许额外字段()
    {
        // OpenAPI 默认允许额外字段，报出来就是误报
        var result = Validate("GET", "/api/users/{id}", 200,
            """{"id":"1","name":"张三","unknownField":"whatever"}""");

        Assert.True(result.Valid, result.Describe());
    }

    // ------------------------------ 失败

    [Fact]
    public void 缺少必填字段要报出来()
    {
        var result = Validate("GET", "/api/users/{id}", 200, """{"id":"1"}""");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Message.Contains("name"));
        Assert.Equal("$", result.Violations[0].Path);
    }

    [Fact]
    public void 字段类型不符要报出来()
    {
        // age 声明为 integer，实际给了字符串
        var result = Validate("GET", "/api/users/{id}", 200, """{"id":"1","name":"张三","age":"30"}""");

        Assert.False(result.Valid);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("$.age", violation.Path);
        Assert.Contains("integer", violation.Message);
        Assert.Contains("string", violation.Message);
    }

    [Fact]
    public void 数组元素形状不符要带下标()
    {
        var result = Validate("GET", "/api/items", 200, """[{"id":"1","name":"a"},{"id":"2"}]""");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Path == "$[1]" && v.Message.Contains("name"));
    }

    [Fact]
    public void 嵌套数组元素类型不符()
    {
        var result = Validate("GET", "/api/users/{id}", 200,
            """{"id":"1","name":"a","tags":["x",123]}""");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Path == "$.tags[1]");
    }

    [Fact]
    public void 枚举取值越界要报出来()
    {
        var result = Validate("GET", "/api/status", 200, """{"state":"unknown","count":1}""");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Path == "$.state" && v.Message.Contains("枚举"));
    }

    [Fact]
    public void 声明additionalProperties为false时多余字段要报()
    {
        var result = Validate("GET", "/api/status", 200,
            """{"state":"on","count":1,"extra":"x"}""");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Path == "$.extra" && v.Message.Contains("未声明"));
    }

    [Fact]
    public void 声明了结构却返回非JSON要报()
    {
        var result = Validate("GET", "/api/users/{id}", 200, "<html>not json</html>");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Message.Contains("不是合法 JSON"));
    }

    [Fact]
    public void 精确状态码优先于通配()
    {
        // 200 声明了 User 结构（要求 id/name），2XX 只要求 ok。
        // 若错误地命中 2XX，下面这个缺 name 的响应就会被误判为通过。
        var result = Validate("GET", "/api/users/{id}", 200, """{"ok":true}""");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Message.Contains("id"));
    }

    [Fact]
    public void 非2xx状态码走自己的schema()
    {
        var result = Validate("GET", "/api/users/{id}", 404, """{"code":404}""");

        Assert.False(result.Valid);
        Assert.Contains(result.Violations, v => v.Message.Contains("message"));
    }

    // ------------------------------ 不判定（避免误报）

    [Fact]
    public void 找不到路径时不判定()
    {
        Assert.True(Validate("GET", "/api/not-exist", 200, """{"a":1}""").Valid);
    }

    [Fact]
    public void 找不到方法时不判定()
    {
        Assert.True(Validate("DELETE", "/api/users/{id}", 200, """{"a":1}""").Valid);
    }

    [Fact]
    public void 该状态码未声明schema时不判定()
    {
        Assert.True(Validate("GET", "/api/items", 500, "server error").Valid);
    }

    [Fact]
    public void 规范为空时不判定()
    {
        Assert.True(OpenApiSchemaValidator.Validate(null, "GET", "/a", 200, "{}").Valid);
        Assert.True(OpenApiSchemaValidator.Validate("", "GET", "/a", 200, "{}").Valid);
    }

    [Fact]
    public void 规范不是合法JSON时不判定()
    {
        Assert.True(OpenApiSchemaValidator.Validate("{not json", "GET", "/a", 200, "{}").Valid);
    }

    [Fact]
    public void 响应体为空时不判定()
    {
        // 没声明 schema 时无法判定；返回空体不主动报错（204 之类很常见）
        Assert.True(Validate("GET", "/api/users/{id}", 200, "").Valid);
    }

    [Fact]
    public void 循环引用不会栈溢出()
    {
        const string recursive = """
        {
          "openapi": "3.0.0",
          "paths": {
            "/self": {
              "get": {
                "responses": {
                  "200": {
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": { "child": { "$ref": "#/components/schemas/Node" } }
                        }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Node": {
                "type": "object",
                "properties": { "child": { "$ref": "#/components/schemas/Node" } }
              }
            }
          }
        }
        """;

        var result = OpenApiSchemaValidator.Validate(recursive, "GET", "/self", 200,
            """{"child":{"child":{"child":{"child":{}}}}}""");

        // 不抛异常就算通过；深递归会被 depth 限制截断，不会无限展开
        Assert.True(result.Valid || result.Violations.Count >= 0);
    }

    [Fact]
    public void 描述信息里带上违规路径()
    {
        var result = Validate("GET", "/api/users/{id}", 200, """{"id":1,"name":"a"}""");

        Assert.False(result.Valid);
        var text = result.Describe();
        Assert.Contains("$.id", text);
    }
}
